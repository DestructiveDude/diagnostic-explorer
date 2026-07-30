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

namespace WidgetSample;

//Widget uses the DiagnosticManager.RegisterAsync method of registering itself with diagnostics
public class Widget : IDisposable, INotifyPropertyChanged
{
    private static readonly string[] _names = new[]
    {
        "Widget X",
        "Widget Y",
        "Widget Z",
        "Widget W",
    };

    private readonly SynchronizationContext _syncContext;
    private DateTime _dateCreated;
    private string _name;
    private Point _size;

    public Widget(int id)
    {
        Id = id;
        _syncContext = SynchronizationContext.Current;

        Randomise();
        var bagName = $"Widget {Id}";
        DiagnosticManager.Register(this, bagName, "Widgets");
    }

    public int Id { get; }

    public string FullName => $"{Name}({Id})";

    [Property(Ignore = true)]
    public string IgnoredProperty => "This value will not be exposed in diagnostics";

    [Property(AllowSet = true)]
    public string Name
    {
        get => _name;
        set
        {
            _name = value;
            OnPropertyChanged("Name");
        }
    }

    [Property(AllowSet = true, FormatString = "{0:d MMM yyyy HH:mm:ss}", Category = "Info")]
    public DateTime DateCreated
    {
        get => _dateCreated;
        set
        {
            _dateCreated = value;
            OnPropertyChanged("DateCreated");
        }
    }

    [Property(AllowSet = true, FormatString = "Located at {0}", Category = "Info")]
    public Point Size
    {
        get => _size;
        set
        {
            _size = value;
            OnPropertyChanged("Size");
        }
    }

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
        var handler = PropertyChanged;
        if (handler != null)
        {
            if (_syncContext != null && _syncContext != SynchronizationContext.Current)
            {
                _syncContext.Post(
                    state => handler(this, new PropertyChangedEventArgs(propertyName)),
                    null
                );
            }
            else
            {
                handler(this, new PropertyChangedEventArgs(propertyName));
            }
        }
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
