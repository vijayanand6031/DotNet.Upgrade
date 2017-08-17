using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml;
using EnvDTE;
using EnvDTE80;
using Microsoft.Build.Evaluation;
using Project = EnvDTE.Project;
using ProjectItem = EnvDTE.ProjectItem;
using Thread = System.Threading.Thread;

namespace Adp.DotNet.Upgrade
{
    public class DotNetFrameworkMigratorService : IDisposable
    {
        private const int MigrateSleep = 200;
        private const int MigrateRetries = 2;

        private Thread _backgroundCaptureThread;
        private bool _backgroundCaptureThreadStop;
        private CancellationToken _token;

        private DTE _applicationObject;
        private readonly ProjectsUpdateList _projectsUpdateList;
        private readonly IEnumerable<FrameworkModel> _frameworkModels;

        private readonly object _syncRoot = new object();

        public event Action<List<ProjectModel>> AddProjectFired;
        public event Action<ProjectModel> UpdateProjectFired;
        public event Action<string> UpdateStateFired;
        public event Action LoadProjectsFired;
        public event Action<string> NotifyLoggerFired;

        public void SetCancellationToken(CancellationToken token)
        {
            _token = token;
        }

        public DotNetFrameworkMigratorService(ProjectsUpdateList pl) : this(pl, new CancellationToken())
        {
        }


        public DotNetFrameworkMigratorService(ProjectsUpdateList pl, CancellationToken token)
        {
            _token = token;
            _backgroundCaptureThreadStop = false;
            this._projectsUpdateList = pl;
            _frameworkModels = LoadTargetFrameWorks();
            _projectsUpdateList.MigrationOptions = new List<string>() {"Build Engine","Environment DTE"};
            _projectsUpdateList.Frameworks = _frameworkModels.ToList();
        }

        private IEnumerable<FrameworkModel> LoadTargetFrameWorks()
        {
            var frameworks = new XmlDocument();
            var folderPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            if (string.IsNullOrEmpty(folderPath)) yield break;

            frameworks.Load(Path.Combine(folderPath, "Frameworks.xml"));
            if (frameworks.DocumentElement == null) yield break;

            foreach (XmlNode node in frameworks.DocumentElement.ChildNodes)
            {
                if (node?.Attributes != null)
                {
                    yield return new FrameworkModel
                    {
                        Id = uint.Parse(node.Attributes["Id"].Value),
                        Name = node.Attributes["Name"].Value
                    };
                }
            }
        }

        public void LoadSolution()
        {
            if (_applicationObject == null)
            {
                OnUpdateStateFired($"Loading Solution from Visual Studio Environment ..... !!!! ");
                _applicationObject = Marshal.GetActiveObject("VisualStudio.DTE.14.0") as EnvDTE.DTE;
                //_applicationObject?.Solution.Open(solutionName);
            }
        }

        public void Start()
        {
            // start the Capture background thread
            _backgroundCaptureThreadStop = false;
            _backgroundCaptureThread = new System.Threading.Thread(BackgroundCaptureThread);
            var frameworkModel = _projectsUpdateList.SelectedFramework;
            _backgroundCaptureThread.Start(frameworkModel);
        }

        public void Stop()
        {
            if (_backgroundCaptureThread != null)
            {
                //_backgroundCaptureThread.Join();
                _backgroundCaptureThread.Abort();
                _backgroundCaptureThread = null;
                _backgroundCaptureThreadStop = true;
            }
        }

        public void LoadProjectsFromSolution(string solutionPath)
        {
            OnUpdateStateFired($"Loading Projects from : {solutionPath} ..... !!!! ");
            var allProjects = new List<ProjectModel>();
            var solutionContent = File.ReadAllText(solutionPath);
            var projReg = new Regex(
                "Project\\(\"\\{[\\w-]*\\}\"\\) = \"([\\w _]*.*)\", \"(.*\\.(cs|vcx|vb)proj)\""
                , RegexOptions.Compiled);
            var matches = projReg.Matches(solutionContent).Cast<Match>();
            var projects = matches.Select(x => x.Groups[2].Value).ToList();
            for (var i = 0; i < projects.Count; ++i)
            {
                if (_token.IsCancellationRequested)
                {
                    _backgroundCaptureThreadStop = true;
                    OnUpdateStateFired("Loading Projects Cancelled");
                    _token.ThrowIfCancellationRequested();
                }
                if (!Path.IsPathRooted(projects[i]))
                {
                    var solutionDir = Path.GetDirectoryName(solutionPath);
                    if (!string.IsNullOrEmpty(solutionDir))
                    {
                        projects[i] = Path.Combine(solutionDir, projects[i]);
                    }
                    projects[i] = Path.GetFullPath(projects[i]);
                    var projectModel = new ProjectModel {Path = Path.GetFullPath(projects[i])};
                    projectModel.Name = Path.GetFileNameWithoutExtension(projectModel.Path);
                    allProjects.Add(projectModel);
                }
            }
            OnAddProjectFired(allProjects);
            //_projectsUpdateList.Projects.Add(projectModel);
            OnUpdateStateFired($"Loading Projects from : {solutionPath} ..... Done ");
        }

        public void UpdateProjectProperties()
        {
            if (_projectsUpdateList?.Projects == null) return;
            OnUpdateStateFired($"Gathering Framework Version for Projects  ..... !!!! ");
            if (_projectsUpdateList?.Projects == null) return;
            foreach (var projectModel in _projectsUpdateList?.Projects)
            {
                OnUpdateStateFired($"Gathering : {projectModel.Name}");
                if (_token.IsCancellationRequested)
                {
                    _backgroundCaptureThreadStop = true;
                    OnUpdateStateFired("Gathering Projects Cancelled");
                    _token.ThrowIfCancellationRequested();
                }
                var projectCollection = new ProjectCollection();
                var proj = projectCollection.LoadProject(projectModel.Path);
                var frameworkProperty = proj.GetProperty("TargetFrameworkVersion");
                if (frameworkProperty != null)
                {
                    projectModel.Framework = new FrameworkModel
                    {
                        Name = $".Net Framework, {frameworkProperty.Name} : [{frameworkProperty.EvaluatedValue}]",
                        Value = frameworkProperty.EvaluatedValue
                    };
                }
                projectModel.Updated = false;
                projectModel.IsSelected = false;
                projectModel.BuildProject = proj;
                OnUpdateProjectFired(projectModel);
            }
            OnUpdateStateFired($"Gathering Framework Version for Projects done !!!! ");
        }

        public void LoadAllProjects()
        {
            lock (_syncRoot)
            {
                _projectsUpdateList.Frameworks = _frameworkModels.ToList();
                _projectsUpdateList.State = "Waiting all projects are loaded...";

                if (_applicationObject.Solution == null)
                {
                    _projectsUpdateList.State = "No solution";
                }
                else
                {
                    ReloadProjects();
                }
                _projectsUpdateList.StartPosition = FormStartPosition.CenterScreen;
                OnUpdateStateFired($"Gathering Framework Version for Projects done !!!! ");
                //_projectsUpdateList.TopMost = true;
            }
        }

        public void ReloadProjects()
        {
            var projectModels = LoadProjects();
            _projectsUpdateList.State = projectModels.Count == 0 ? "No .Net projects" : String.Empty;
            OnAddProjectFired(projectModels);
            //_projectsUpdateList.Projects = projectModels;
        }

        private List<ProjectModel> LoadProjects()
        {
            OnUpdateStateFired($"Loading Projects from Solution ..... !!!! ");
            if (_projectsUpdateList.IsEnvDTE)
            {
                var projects = _applicationObject.Solution.Projects;

                if (projects.Count == 0)
                {
                    return new List<ProjectModel>();
                }

                var projectModels = MapProjects(projects.OfType<Project>());

                projectModels = projectModels
                    .Where(pm => pm.HasFramework)
                    .ToList();
                return projectModels;
            }
            else
            {
                var projects = _projectsUpdateList.Projects;
                foreach(var pm in projects)
                {
                    var fm = _projectsUpdateList.SelectedFramework;
                    if (fm == null) continue;
                    if (pm.Framework.Value.Equals(fm.Value))
                    {
                        pm.Updated = true;
                    }
                    else
                    {
                        pm.Updated = false;
                    }
                }
                return projects;
            }
        }

        private List<ProjectModel> MapProjects(IEnumerable<Project> projects)
        {
            var projectModels = new List<ProjectModel>();
            foreach (var p in projects)
            {
                if (p == null)
                    continue;

                if (_token.IsCancellationRequested)
                {
                    _backgroundCaptureThreadStop = true;
                    OnUpdateStateFired("Gathering Projects Cancelled");
                    _token.ThrowIfCancellationRequested();
                }


                if (_token.IsCancellationRequested)
                {
                    _backgroundCaptureThreadStop = true;
                    OnUpdateStateFired("Gathering Projects Cancelled");
                    _token.ThrowIfCancellationRequested();
                }

                if (p.Kind == ProjectKinds.vsProjectKindSolutionFolder)
                {
                    var projectItems = p.ProjectItems.OfType<ProjectItem>();
                    var subProjects = projectItems.Select(pi => pi.SubProject);
                    projectModels.AddRange(MapProjects(subProjects));
                }
                else
                {
                    OnUpdateStateFired($"Gathering : {p.Name}");
                    var projectModel = MapProject(p, _frameworkModels.FirstOrDefault());
                    if (_projectsUpdateList.Visible)
                    {
                        projectModel = MapProject(p, _projectsUpdateList.SelectedFramework);
                    }
                    if (projectModel != null)
                    {
                        projectModels.Add(projectModel);
                    }
                }
            }
            return projectModels;
        }

        private static ProjectModel MapProject(Project p, FrameworkModel fm)
        {
            //if (p.Name.Equals(Assembly.GetCallingAssembly().GetName().Name, StringComparison.InvariantCultureIgnoreCase))
            //    return null;


            var projectModel = new ProjectModel
            {
                Name = p.Name,
                DteProject = p,
            };
            if (p.Properties == null) return projectModel;

            // not applicable for current project
            if (p.Properties.Item("TargetFramework") == null ||
                p.Properties.Item("TargetFrameworkMoniker") == null) return projectModel;

            try
            {
                var frameworkModel = new FrameworkModel
                {
                    Id = (uint) p.Properties.Item("TargetFramework").Value,
                    Name = (string) p.Properties.Item("TargetFrameworkMoniker").Value
                };
                projectModel.Framework = frameworkModel;
                if (projectModel.Framework.Id == fm.Id)
                {
                    projectModel.Updated = true;
                }
                else
                {
                    projectModel.Updated = false;
                }
            }
            catch (ArgumentException e) //possible when project still loading
            {
                Debug.WriteLine("ArgumentException on " + projectModel + e);
            }
            catch (InvalidCastException e) //for some projects with wrong types
            {
                Debug.WriteLine("InvalidCastException on " + projectModel + e);
            }
            return projectModel;
        }

        private async void BackgroundCaptureThread(object frameworkModel)
        {
            if (frameworkModel == null) return;
            while (!_backgroundCaptureThreadStop)
            {
                var selectedFrameworkModel = frameworkModel as FrameworkModel;
                if (selectedFrameworkModel == null)
                {
                    _backgroundCaptureThreadStop = true;
                    return;
                }
                OnUpdateStateFired( $"Updating...... !!!! {selectedFrameworkModel.Name}");

                var enumerable = _projectsUpdateList.Projects.Where(p => p.IsSelected);

                //var task = Task.WhenAll(enumerable.Select((projectModel) => UpdateFramework(projectModel, selectedFrameworkModel)).ToArray());
                //task.Wait(_token);
                var projectNum = 1;
                foreach (var projectModel in enumerable)
                {
                    if (_token.IsCancellationRequested)
                    {
                        _backgroundCaptureThreadStop = true;
                        OnUpdateStateFired("Migration Cancelled");
                        _token.ThrowIfCancellationRequested();
                    }

                    var migrated = await UpdateFramework(projectModel,selectedFrameworkModel);
                    if (migrated)
                    {
                        OnUpdateStateFired($"Project : {projectNum} - Updating... {projectModel.Name} done");
                        projectModel.Updated = true;
                        OnUpdateProjectFired(projectModel);
                    }
                    else
                    {
                        OnUpdateStateFired($"Project : {projectNum} - Updating... {projectModel.Name} Failed");
                        projectModel.Updated = false;
                        OnUpdateProjectFired(projectModel);
                    }
                    projectNum++;
                }
                
                OnUpdateStateFired($"Migration Finished");
                //OnLoadProjectsFired();
                _backgroundCaptureThreadStop = true;

            }
        }

        private Task<bool> UpdateFramework(ProjectModel pm, FrameworkModel fm)
        {
            return Task.Run(() =>
            {
                var retryCount = 1;
                var migrated = false;
                while (retryCount <= MigrateRetries)
                {
                    try
                    {
                        if (_token.IsCancellationRequested)
                        {
                            _backgroundCaptureThreadStop = true;
                            OnUpdateStateFired("Migration Cancelled");
                            _token.ThrowIfCancellationRequested();
                        }
                        if (_projectsUpdateList.IsEnvDTE)
                        {
                            pm.DteProject.Properties.Item("TargetFrameworkMoniker").Value = fm.Name;
                        }
                        else
                        {
                            pm.BuildProject.SetProperty("TargetFrameworkVersion", fm.Value);
                            pm.BuildProject.Save();
                        }
                        pm.Framework = fm;
                        pm.Updated = true;
                        migrated = true;
                        break;
                    }
                    catch (COMException e) //possible "project unavailable" for unknown reasons
                    {
                        var msg = $"Attempt : {retryCount} COMException on {pm.Name} {e}";
                        Debug.WriteLine(msg);
                        OnUpdateStateFired(msg);
                        migrated = false;
                        Thread.Sleep(MigrateSleep);
                        retryCount++;
                    }
                }
                return migrated;
            }, _token);
        }

        protected virtual void OnUpdateProjectFired(ProjectModel pm)
        {
            UpdateProjectFired?.Invoke(pm);
        }

        protected virtual void OnUpdateStateFired(string msg)
        {
            UpdateStateFired?.Invoke(msg);
            OnNotifyLoggerFired(msg);
        }

        #region == IDisposable Members ==

        public void Dispose()
        {
            this.Dispose(true);
        }

        protected virtual void Dispose(bool dispose)
        {
            if (dispose)
            {
                _applicationObject = null;
                if (_backgroundCaptureThread != null)
                {
                    _backgroundCaptureThread.Join();
                    _backgroundCaptureThread = null;
                }
                _backgroundCaptureThreadStop = false;


                UpdateProjectFired = null;
                UpdateStateFired = null;
                LoadProjectsFired = null;

                GC.SuppressFinalize(this);
            }
        }

        #endregion

        protected virtual void OnLoadProjectsFired()
        {
            LoadProjectsFired?.Invoke();
        }

        protected virtual void OnNotifyLoggerFired(string obj)
        {
            NotifyLoggerFired?.Invoke(obj);
        }

        protected virtual void OnAddProjectFired(List<ProjectModel> obj)
        {
            AddProjectFired?.Invoke(obj);
        }
    }
}

