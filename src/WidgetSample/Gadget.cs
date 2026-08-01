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

//Widget extends DiagnosticManager in order to register itself with diagnostics
public class Gadget : IDisposable, INotifyPropertyChanged
{
    private static readonly string[] _names = new[] { "Gadget X", "Gadget Y", "Gadget Z", "Gadget W" };

    private static readonly string[] _purposes = new[] { "Technical", "Muckabout", "Stuff" };
    private readonly SynchronizationContext _syncContext;

    private string _name;

    private string _purpose;

    public Gadget(int id)
    {
        Id = id;
        _syncContext = SynchronizationContext.Current;

        Name = GetRandom(_names);
        Purpose = GetRandom(_purposes);
        DiagnosticManager.Register(this, string.Format("Gadget {0}", Id), "Gadgets");
    }

    public int Id { get; }

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

    [Property(AllowSet = true)]
    public string Purpose
    {
        get => _purpose;
        set
        {
            _purpose = value;
            OnPropertyChanged("Purpose");
        }
    }

    public event PropertyChangedEventHandler PropertyChanged;

    [DiagnosticMethod]
    public void Randomise()
    {
        Name = GetRandom(_names);
        Purpose = GetRandom(_purposes);
    }

    [DiagnosticMethod]
    public void Clear()
    {
        Name = null;
        Purpose = null;
    }

    private void OnPropertyChanged(string propertyName)
    {
        var handler = PropertyChanged;
        if (handler != null)
        {
            if (_syncContext != null && _syncContext != SynchronizationContext.Current)
            {
                _syncContext.Post(state => handler(this, new PropertyChangedEventArgs(propertyName)), null);
            }
            else
            {
                handler(this, new PropertyChangedEventArgs(propertyName));
            }
        }
    }

    private string GetRandom(string[] items)
    {
        var index = ThreadSafeRandom.Next(0, items.Length);
        return items[index];
    }

    public override string ToString()
    {
        return string.Format("Gadget {0}", Id);
    }

    #region IDisposable Members

    // Mirror Widget: dispose unregisters the gadget from diagnostics so removal is
    // deterministic (via RemoveItem) instead of relying on a GC pass to drop the
    // DiagnosticManager registration.
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    ~Gadget()
    {
        Dispose(false);
    }

    protected void Dispose(bool disposing)
    {
        if (disposing)
        {
            DiagnosticManager.Unregister(this);
        }
    }

    #endregion
}
