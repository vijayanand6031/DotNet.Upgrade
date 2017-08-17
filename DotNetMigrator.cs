using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using EnvDTE;

namespace Adp.DotNet.Upgrade
{
    public partial class ProjectsUpdateList : Form
    {
        private readonly DotNetFrameworkMigratorService _migratorService;
        private CancellationTokenSource _cancellationTokenSource;

        public delegate void UpdateProjectStateCallback(ProjectModel pm);
        public delegate void UpdateStateCallback(string msg);
        public delegate void LoadProjectsCallback();
        public delegate void LoggerUpdatedCallback(string msg);
        public delegate void AddProjectCallback(List<ProjectModel> pmList);

        public ProjectsUpdateList()
        {
            InitializeComponent();
            dataGridView1.AutoGenerateColumns = false;
            comboBox1.DropDownStyle = ComboBoxStyle.DropDownList;
            comboBox2.DropDownStyle = ComboBoxStyle.DropDownList;
            backgroundWorker1.WorkerReportsProgress = true;
            backgroundWorker1.WorkerSupportsCancellation = true;
            backgroundWorker1.DoWork += backgroundWorker1_DoWork;
            backgroundWorker1.RunWorkerCompleted += backgroundWorker1_RunWorkerCompleted;
            _migratorService = new DotNetFrameworkMigratorService(this);
            _migratorService.UpdateProjectFired += SetProjectState;
            _migratorService.UpdateStateFired += UpdateState;
            _migratorService.LoadProjectsFired += LoadAllProjects;
            _migratorService.NotifyLoggerFired += UpdateLoggerInfo;
            _migratorService.AddProjectFired += AddProject;
        }

        public bool IsBuildEngine { set; get; }

        public bool IsEnvDTE { set; get; }

        public List<string> MigrationOptions
        {
            set { comboBox2.DataSource = value; }
        }

        public List<FrameworkModel> Frameworks
        {
            set { comboBox1.DataSource = value; }
        }

        public void RefreshDataGrid()
        {
            dataGridView1.Refresh();
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (backgroundWorker1.IsBusy)
            {
                backgroundWorker1.CancelAsync();
            }

            if (_migratorService != null)
            {
                _migratorService.Stop();
                _migratorService.Dispose();
            }

            Projects = null;
            Frameworks = null;
        }

        //handler method to run when work has completed  
        private void AddProject(List<ProjectModel> pmList)
        {
            if (InvokeRequired)
            {
                Invoke(new AddProjectCallback(this.AddProjectModel), pmList);
            }
            else
            {
                AddProjectModel(pmList);
            }
        }

        private void AddProjectModel(List<ProjectModel> pmList)
        {
            Projects = pmList;
            dataGridView1.Refresh();
        }

        //handler method to run when work has completed  
        private void UpdateLoggerInfo(string msg)
        {
            if (InvokeRequired)
            {
                Invoke(new LoggerUpdatedCallback(this.UpdateLoggerText), msg);
            }
            else
            {
                UpdateLoggerText(msg);
            }
        }

        private void UpdateLoggerText(string message)
        {
            PrintLogger(message);
        }

        //handler method to run when work has completed  
        private void LoadAllProjects()
        {
            //string msg = (string)sender;
            if (InvokeRequired)
            {
                Invoke(new LoadProjectsCallback(this.LoadProjects));
            }
            else
            {
                LoadProjects();
            }
        }

        public void LoadProjects()
        {
            if (backgroundWorker1.IsBusy != true)
            {
                // Start the asynchronous operation.
                backgroundWorker1.RunWorkerAsync();
            }
        }

        // This event handler deals with the results of the background operation.
        private void backgroundWorker1_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            if (e.Cancelled == true)
            {
                label1.Text = "Canceled!";
            }
            else if (e.Error != null)
            {
                label1.Text = "Error: " + e.Error.Message;
            }
            else
            {
                label1.Text = "Loading Projects Done!";
            }
        }


        // This event handler is where the time-consuming work is done.
        private void backgroundWorker1_DoWork(object sender, DoWorkEventArgs e)
        {
            BackgroundWorker worker = sender as BackgroundWorker;

            if (worker != null && worker.CancellationPending == true)
            {
                e.Cancel = true;
                return;
            }
            else
            {
                // Perform a time consuming operation and report progress.
                if (IsEnvDTE)
                {
                    _migratorService.LoadSolution();
                    _migratorService.LoadAllProjects();
                }
                else
                {
                    _migratorService.LoadProjectsFromSolution(txt_solutionName.Text);
                    _migratorService.UpdateProjectProperties();
                    _migratorService.ReloadProjects();
                }
            }

        }

        //handler method to run when work has completed  
        private void SetProjectState(ProjectModel pm)
        {
            //string msg = (string)sender;
            if (InvokeRequired)
            {
                Invoke(new UpdateProjectStateCallback(this.SetProjectStatus), pm);
            }
            else
            {
                SetProjectStatus(pm);
            }
        }

        public void SetProjectStatus(ProjectModel pm)
        {
            dataGridView1.Refresh();
        }


        //handler method to run when work has completed  
        private void UpdateState(string msg)
        {
            //string msg = (string)sender;
            if (InvokeRequired)
            {
                Invoke(new UpdateStateCallback(this.UpdateStateInformation), msg);
            }
            else
            {
                UpdateStateInformation(msg);
            }
        }

        public void UpdateStateInformation(string msg)
        {
            State = msg;
        }

        public List<ProjectModel> Projects
        {
            set
            {
                var wrapperBindingList = new SortableBindingList<ProjectModel>(value);
                try
                {
                    dataGridView1.DataSource = wrapperBindingList;
                    dataGridView1.Refresh();
                }
                catch (InvalidOperationException)
                {
                    Invoke(new EventHandler(delegate
                    {
                        dataGridView1.DataSource = wrapperBindingList;
                        dataGridView1.Refresh();
                    }));
                }
            }
            get
            {
                SortableBindingList<ProjectModel> wrapperBindingList = null;
                try
                {
                    wrapperBindingList = (SortableBindingList<ProjectModel>) dataGridView1.DataSource;

                }
                catch (InvalidOperationException)
                {
                    Invoke(new EventHandler(delegate
                    {
                        wrapperBindingList = (SortableBindingList<ProjectModel>) dataGridView1.DataSource;
                    }));
                }

                return wrapperBindingList?.WrappedList;
            }
        }

        public FrameworkModel SelectedFramework
        {
            get
            {
                FrameworkModel model = null;
                Invoke(new EventHandler(delegate
                {
                    model = (FrameworkModel) comboBox1.SelectedItem;
                }));
                return model;
            }
        }

        public string State
        {
            set
            {
                try
                {
                    label1.Text = value;
                }
                catch (InvalidOperationException)
                {
                    Invoke(new EventHandler(delegate
                    {
                        label1.Text = value;
                    }));
                }
            }
        }

        private void migrateButton_Click(object sender, EventArgs e)
        {
            var sel = SetMigrationType();
            if (!sel)
            {
                MessageBox.Show(Properties.Resources.migration_Option_using_Build_Engine_or_DTE);
                comboBox2.Focus();
                return;
            }
            if (IsBuildEngine)
            {
                var solutionName = txt_solutionName.Text;
                if (string.IsNullOrEmpty(solutionName))
                {
                    MessageBox.Show(Properties.Resources.Please_select_a_valid_Visual_Studio_solution_file);
                    comboBox2.Focus();
                    return;
                }
            }

            var nullProject = Projects.Exists(p => p.DteProject == null);
            if (IsEnvDTE && nullProject)
            {
                MessageBox.Show(Properties.Resources.Cannot_determine_Projects_using_Envinromnent_DTE);
                comboBox2.Focus();
                return;
            }

            if (_migratorService != null)
            {
                _migratorService.Stop();
                try
                {
                    _cancellationTokenSource = new CancellationTokenSource();
                    _migratorService.SetCancellationToken(_cancellationTokenSource.Token);
                    _migratorService.Start();
                }
                catch (OperationCanceledException canccelException)
                {
                    var msg = $"OperationCanceledException on Migrate: {canccelException}";
                    Debug.WriteLine(msg);
                    UpdateState(msg);
                    throw;
                }
                catch (Exception exception)
                {
                    var msg = $"Exception on Migrate : {exception}";
                    Debug.WriteLine(msg);
                    UpdateState(msg);
                    throw;
                }
            }
        }

        private void selectAllButton_Click(object sender, EventArgs e)
        {
            foreach (var projectModel in Projects)
            {
                projectModel.IsSelected = true;
            }
            dataGridView1.Refresh();
        }

        private void PrintLogger(string message)
        {
            label1.Text = message;
            var sb = new StringBuilder(txt_log.Text);
            sb.AppendLine(message);
            txt_log.Text = sb.ToString();
            txt_log.SelectionStart = txt_log.Text.Length;
            txt_log.ScrollToCaret();
        }
        private void selectNoneButton_Click(object sender, EventArgs e)
        {
            foreach (var projectModel in Projects)
            {
                projectModel.IsSelected = false;
            }
            dataGridView1.Refresh();
        }

        private void reloadButton_Click(object sender, EventArgs e)
        {
            _migratorService?.ReloadProjects();
        }

        private void comboBox1_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (comboBox1.SelectedValue == null) return;
            if (string.IsNullOrWhiteSpace(comboBox1.Text)) return;
            var fm = (FrameworkModel)comboBox1.SelectedValue;
            if (fm == null) return;
            if (Projects == null) return;
            if (Projects.Count <= 0) return;
            //_migratorService?.ReloadProjects();

            foreach (var p in Projects)
            {
                if (!p.HasFramework || p.Framework == null) continue;
                if (IsEnvDTE)
                {
                    p.Updated = p.Framework.Id == fm.Id;
                }
                else
                {
                    if (p.Framework.Value.Equals(fm.Value))
                    {
                        p.Updated = true;
                    }
                }
            }
            dataGridView1.Refresh();
        }

        private void cancelButton_Click(object sender, EventArgs e)
        {
            _cancellationTokenSource.Cancel(true);
            if (backgroundWorker1.WorkerSupportsCancellation == true)
            {
                // Cancel the asynchronous operation.
                backgroundWorker1.CancelAsync();
            }
            //_migratorService?.Stop();
        }

        private void button1_Click(object sender, EventArgs e)
        {
            if (comboBox2.SelectedValue == null) return;
            if (string.IsNullOrWhiteSpace(comboBox2.Text)) return;
            var migrateType = (string)comboBox2.SelectedValue;
            if (string.IsNullOrEmpty(migrateType))
            {
                MessageBox.Show(Properties.Resources.migration_Option_using_Build_Engine_or_DTE);
                comboBox2.Focus();
                return;
            }
            else
            {
                if (IsBuildEngine)
                {
                    var dr = fd_openSolution.ShowDialog();
                    if (dr == DialogResult.OK)
                    {
                        var fileName = fd_openSolution.FileName;
                        txt_solutionName.Text = fileName;
                    }
                    else
                    {
                        MessageBox.Show(Properties.Resources.Please_select_a_valid_Visual_Studio_solution_file);
                        comboBox2.Focus();
                        return;
                    }
                }
            }
            try
            {
                _cancellationTokenSource = new CancellationTokenSource();
                _migratorService.SetCancellationToken(_cancellationTokenSource.Token);
                LoadProjects();
            }
            catch (OperationCanceledException canccelException)
            {
                var msg = $"OperationCanceledException on Load: {canccelException}";
                Debug.WriteLine(msg);
                UpdateState(msg);
                throw;
            }
            catch (Exception exception)
            {
                var msg = $"Exception on Load: {exception}";
                Debug.WriteLine(msg);
                UpdateState(msg);
                throw;
            }
        }

        private bool SetMigrationType()
        {
            if (comboBox2.SelectedValue == null) return false;
            if (string.IsNullOrWhiteSpace(comboBox2.Text)) return false;
            var migrateType = (string)comboBox2.SelectedValue;
            if (!string.IsNullOrEmpty(migrateType))
            {
                if (migrateType.Equals("Build Engine", StringComparison.InvariantCultureIgnoreCase))
                {
                    IsBuildEngine = true;
                    IsEnvDTE = false;
                }
                else
                {
                    IsBuildEngine = false;
                    IsEnvDTE = true;
                }
            }
            return true;
        }

        private void comboBox2_SelectedIndexChanged(object sender, EventArgs e)
        {
            SetMigrationType();
        }
    }



    public class ProjectModel : INotifyPropertyChanged
    {
        private bool _isSelected;
        private string _name;

        public bool IsSelected
        {
            get { return _isSelected; }
            set
            {
                _isSelected = value;
                OnPropertyChanged();
            }
        }

        public string Path { get; set; }

        public string Name
        {
            get { return _name; }
            set
            {
                _name = value;
                OnPropertyChanged();
            }
        }

        public FrameworkModel Framework { get; set; }

        public bool HasFramework => Framework != null;

        public Project DteProject { get; set; }

        public Microsoft.Build.Evaluation.Project BuildProject { get; set; }

        public override string ToString()
        {
            return $"{Name}";
        }

        private bool _updated;
        public bool Updated
        {
            get { return _updated; }
            set
            {
                _updated = value;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            var handler = PropertyChanged;
            if (handler != null)
                handler(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class FrameworkModel : IComparable
    {
        public string Value { get; set; }

        private string _name;

        public string Name
        {
            get { return _name; }
            set
            {
                _name = value;
                if (!string.IsNullOrEmpty(_name))
                {
                    if (string.IsNullOrEmpty(Value))
                    {
                        var splitValue = _name.Split('=');
                        if (splitValue != null && splitValue.Length == 2)
                        {
                            Value = splitValue[1];
                        }
                    }
                }
            }
        }

        public uint Id { get; set; }

        public override string ToString()
        {
            return $"{Name}";
        }

        // support comparison so we can sort by properties of this type
        public int CompareTo(object obj)
        {
            return StringComparer.Ordinal.Compare(this.Name, ((FrameworkModel) obj).Name);
        }
    }
}

