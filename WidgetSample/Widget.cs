#region Copyright

// Diagnostic Explorer, a .Net diagnostic toolset
// Copyright (C) 2010 Cameron Elliot
// 
// This file is part of Diagnostic Explorer.
// 
// Diagnostic Explorer is free software: you can redistribute it and/or modify
// it under the terms of the GNU Lesser General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
// 
// Diagnostic Explorer is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU Lesser General Public License for more details.
// 
// You should have received a copy of the GNU Lesser General Public License
// along with Diagnostic Explorer.  If not, see <http://www.gnu.org/licenses/>.
// 
// http://diagexplorer.sourceforge.net/

#endregion

using System;
using System.ComponentModel;
using System.Drawing;
using DiagnosticExplorer;
using DiagnosticExplorer.Props;

namespace WidgetSample;

//Widget uses the DiagnosticManager.RegisterAsync method of registering itself with diagnostics
public class Widget : IDisposable, INotifyPropertyChanged
{
    private static readonly string[] _names = new[] { "Widget X", "Widget Y", "Widget Z", "Widget W" };
    private readonly int _id;
    private DateTime _dateCreated;
    private string _name;
    private Point _size;

    public Widget(int id)
    {
        _id = id;

        Randomise();
        string bagName = $"Widget {_id}";
        DiagnosticManager.Register(this, bagName, "Widgets");
    }

    public int Id => _id;

    public string FullName => $"{Name}({_id})";

    [DiagnosticMethod]
    public void Randomise()
    {
        Name = _names[ThreadSafeRandom.Next(0, _names.Length)];
        DateCreated = DateTime.Now.AddMinutes(ThreadSafeRandom.Next(0, 10000));
        Size = new Point(ThreadSafeRandom.Next(), ThreadSafeRandom.Next());
    }

    [DiagnosticMethod]
    public void Clear()
    {
        Name = null;
        DateCreated = DateTime.Now;
    }

    [Property(Ignore = true)]
    public string IgnoredProperty => "This value will not be exposed in diagnostics";

    [Property(AllowSet = true)]
    public string Name
    {
        get => _name;
        set {
            _name = value;
            OnPropertyChanged("Name");
        }
    }

    [Property(AllowSet = true, FormatString = "{0:d MMM yyyy HH:mm:ss}", Category = "Info")]
    public DateTime DateCreated
    {
        get => _dateCreated;
        set {
            _dateCreated = value;
            OnPropertyChanged("DateCreated");
        }
    }

    [Property(AllowSet = true, FormatString = "Located at {0}", Category = "Info")]
    public Point Size
    {
        get => _size;
        set {
            _size = value;
            OnPropertyChanged("Size");
        }
    }

    #region IDisposable Members

    public void Dispose()
    {
        Dispose(true);
    }

    #endregion

    #region INotifyPropertyChanged Members

    public event PropertyChangedEventHandler PropertyChanged;

    #endregion

    ~Widget()
    {
        Dispose(false);
    }

    private void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    protected void Dispose(bool disposing)
    {
        if (disposing)
        {
            DiagnosticManager.Unregister(this);
        }
    }

    public override string ToString()
    {
        return string.Format("Widget {0} ({1})", Id, Size);
    }

    [DiagnosticMethod]
    public void Reset()
    {
        Name = null;
        Size = new Point(0, 0);
        DateCreated = new DateTime(2000, 1, 1);
    }

    [DiagnosticMethod]
    public void Reset(string name, DateTime dateCreated)
    {
        Name = name;
        DateCreated = dateCreated;
    }
}