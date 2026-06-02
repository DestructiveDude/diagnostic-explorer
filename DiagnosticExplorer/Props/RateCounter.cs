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
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Timers;

namespace DiagnosticExplorer
{
	public class RateCounter
	{
		// The static timer is intended to run for the entire process lifetime to drive rate calculation.
		private static readonly Timer _timer;
		private static readonly List<WeakReference> _counters = new List<WeakReference>();
		private DateTime _lastCheck = DateTime.UtcNow;
		private readonly int[] _counts;
		private readonly TimeSpan[] _times;
		private int _index;
		public event EventHandler<RateSampleEventArgs> SampleCollected;

		static RateCounter()
		{
			_timer = new Timer();
			_timer.Elapsed += Run;
			_timer.Interval = 1000;
		}

		public RateCounter(int secondsAverage)
		{
			// Must be > 0: zero gives zero-length buffers, so `_index % _counts.Length` throws
			// DivideByZeroException inside Run's swallowed try/catch and the counter silently never
			// advances; negative throws OverflowException at the array allocation below.
			if (secondsAverage <= 0)
				throw new ArgumentOutOfRangeException(nameof(secondsAverage), secondsAverage,
					"secondsAverage must be greater than zero.");

			_counts = new int[secondsAverage];
			_times = new TimeSpan[secondsAverage];

			lock (_counters)
			{
				_counters.Add(new WeakReference(this));
				if (_counters.Count == 1)
					_timer.Start();
			}
		}


		private void Increment()
		{
			// Lock order: always acquire _counters lock before _counts lock.
			// This method is called from Run() which already holds the _counters lock.
			lock (_counts)
			{
				_times[_index % _counts.Length] = DateTime.UtcNow - _lastCheck;
				CalcRate();
				_index++;
				_counts[_index % _counts.Length] = 0;
				_times[_index % _counts.Length] = TimeSpan.Zero;
				_lastCheck = DateTime.UtcNow;

				InvokeSampleCollected();
			}
		}

		private void InvokeSampleCollected()
		{
			try
			{
				EventHandler<RateSampleEventArgs> sampleCollectedHandler = SampleCollected;

				if (sampleCollectedHandler != null)
				{
					RateSampleEventArgs args = new RateSampleEventArgs(Rate, GetRates(_times.Length));
					foreach (EventHandler<RateSampleEventArgs> handler in sampleCollectedHandler.GetInvocationList())
						// Delegate.BeginInvoke throws PlatformNotSupportedException on .NET Core/5+.
						// Task.Run gives the same fire-and-forget async dispatch portably.
						Task.Run(() => handler(this, args));
				}
			}
			catch (Exception ex)
			{
				Debug.WriteLine(ex);
			}
		}

		private void CalcRate()
		{
			double r = _counts.Sum();
			TimeSpan totalTime = _times.Aggregate((t1, t2) => t1 + t2);

			_rate = totalTime == TimeSpan.Zero ? 0 : r / totalTime.TotalSeconds;
		}

		public void Register(int count)
		{
			// Ignore non-positive counts: a negative would corrupt the bucket (and diverge from
			// Total, which only ever counted count > 0), producing negative rates. Zero is a no-op.
			if (count <= 0)
				return;

			lock (_counts)
			{
				_total += (ulong) count;
				_counts[_index % _counts.Length] += count;
			}
		}

		/// <summary>
		/// Gets the last n seconds rates
		/// </summary>
		/// <param name="seconds">The number of seconds worth of data to fetch</param>
		/// <returns>A list of rates for the last n seconds, starting with the latest</returns>
		public int[] GetRates(int seconds)
		{
			lock (_counts)
			{
				return GetRates(seconds, _index, _counts);
			}
		}

		public static int[] GetRates(int seconds, int currentIndex, int[] values)
		{
			// Clamp to the samples actually recorded (currentIndex) AND to the ring-buffer capacity
			// (values.Length). Without the capacity clamp, once the index wraps we would walk back
			// past the buffer size and re-read ring slots as if they were distinct samples,
			// fabricating history the buffer never held.
			seconds = Math.Min(seconds, Math.Min(currentIndex, values.Length));
			int[] results = new int[seconds];
			
			for (int i = 0; i < results.Length; i++)
					results[i] = values[(currentIndex - 1 - i) % values.Length];
			
			return results;
		}

		private double _rate;
		private ulong _total;

		// Read under the same lock the writers (CalcRate/Register) hold: Rate (double) and Total
		// (ulong) are 64-bit, so an unlocked read can tear on a 32-bit host.
		public double Rate { get { lock (_counts) return _rate; } }

		public ulong Total { get { lock (_counts) return _total; } }

		private static void Run(object state, ElapsedEventArgs e)
		{
			try
			{
				// Lock order: always acquire _counters lock before _counts lock.
				lock (_counters)
				{
					for (int i = _counters.Count - 1; i >= 0; i--)
					{
						WeakReference r = _counters[i];
						RateCounter counter = (RateCounter) r.Target;
						if (counter == null)
							_counters.RemoveAt(i);
						else
							counter.Increment();
					}
					if (_counters.Count == 0)
						_timer.Stop();
				}
			}
			catch (Exception ex)
			{
				Debug.WriteLine(ex);
			}
		}
	}
}