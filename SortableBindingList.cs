using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Reflection;

namespace Adp.DotNet.Upgrade
{
    /// <summary>
    ///  List wrapper class to provide basic sorting operations when bound to a DataGrid view.  
    ///  From the Apache 2.0 licensed project http://betimvwframework.codeplex.com/
    ///  Small changes to make it easier to use in the TargetFrameworkMigrator project.
    /// </summary>
    public class SortableBindingList<T> : BindingList<T>
    {
        private readonly Dictionary<Type, PropertyComparer<T>> _comparers;
        private bool _isSorted;
        private ListSortDirection _listSortDirection;
        private PropertyDescriptor _propertyDescriptor;

        public SortableBindingList(List<T> wrappedList)
            : base(wrappedList)
        {
            this.WrappedList = wrappedList;
            this._comparers = new Dictionary<Type, PropertyComparer<T>>();
        }

        public List<T> WrappedList { get; }

        protected override bool SupportsSortingCore => true;

        protected override bool IsSortedCore => this._isSorted;

        protected override PropertyDescriptor SortPropertyCore => this._propertyDescriptor;

        protected override ListSortDirection SortDirectionCore => this._listSortDirection;

        protected override bool SupportsSearchingCore => true;

        protected override void ApplySortCore(PropertyDescriptor property, ListSortDirection direction)
        {
            var itemsList = (List<T>)this.Items;

            var propertyType = property.PropertyType;
            PropertyComparer<T> comparer;
            if (!this._comparers.TryGetValue(propertyType, out comparer))
            {
                comparer = new PropertyComparer<T>(property, direction);
                this._comparers.Add(propertyType, comparer);
            }

            comparer.SetPropertyAndDirection(property, direction);
            itemsList.Sort(comparer);

            this._propertyDescriptor = property;
            this._listSortDirection = direction;
            this._isSorted = true;

            this.OnListChanged(new ListChangedEventArgs(ListChangedType.Reset, -1));
        }

        protected override void RemoveSortCore()
        {
            this._isSorted = false;
            this._propertyDescriptor = base.SortPropertyCore;
            this._listSortDirection = base.SortDirectionCore;

            this.OnListChanged(new ListChangedEventArgs(ListChangedType.Reset, -1));
        }

        protected override int FindCore(PropertyDescriptor prop, object key)
        {
            if (prop == null) throw new ArgumentNullException(nameof(prop));
            var count = this.Count;
            for (var i = 0; i < count; ++i)
            {
                var element = this[i];
                var value = prop.GetValue(element);
                if (value != null && value.Equals(key))
                {
                    return i;
                }
            }

            return -1;
        }
    }

    /// <summary>
    /// helper class for supporting comparison operations in SortableBindingList
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class PropertyComparer<T> : IComparer<T>
    {
        private readonly IComparer _comparer;
        private PropertyDescriptor _propertyDescriptor;
        private int _reverse;

        public PropertyComparer(PropertyDescriptor property, ListSortDirection direction)
        {
            this._propertyDescriptor = property;
            var comparerForPropertyType = typeof(Comparer<>).MakeGenericType(property.PropertyType);
            this._comparer = (IComparer)comparerForPropertyType.InvokeMember("Default", BindingFlags.Static | BindingFlags.GetProperty | BindingFlags.Public, null, null, null);
            this.SetListSortDirection(direction);
        }

        #region IComparer<T> Members

        public int Compare(T x, T y)
        {
            return this._reverse * this._comparer.Compare(this._propertyDescriptor.GetValue(x), this._propertyDescriptor.GetValue(y));
        }

        #endregion

        private void SetPropertyDescriptor(PropertyDescriptor descriptor)
        {
            this._propertyDescriptor = descriptor;
        }

        private void SetListSortDirection(ListSortDirection direction)
        {
            this._reverse = direction == ListSortDirection.Ascending ? 1 : -1;
        }

        public void SetPropertyAndDirection(PropertyDescriptor descriptor, ListSortDirection direction)
        {
            this.SetPropertyDescriptor(descriptor);
            this.SetListSortDirection(direction);
        }
    }
}
