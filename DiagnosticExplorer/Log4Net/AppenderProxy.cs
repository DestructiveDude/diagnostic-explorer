using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Net.Security;
using DiagnosticExplorer.Util;
using log4net.Appender;
using log4net.Core;

namespace DiagnosticExplorer.Log4Net
{
	public struct AppendResult
	{
		public AppendResult(bool success) : this()
		{
			Success = success;
		}

		public AppendResult(bool success, string message)
		{
			Success = success;
			Message = message;
		}

		public bool Success { get; }
		public string Message { get; }
	}

	[DiagnosticClass(AttributedPropertiesOnly = true)]
	public abstract class AppenderProxyBase
	{
		protected TimeSpan _timeout;
		protected DateTime? _errorTime;

		// Guards the failover state (_isInError + the nullable _errorTime) and the diagnostic
		// timestamps (_lastError + _lastMessageSent). These are written by the append thread
		// (DoAppend) and Reactivate (a [DiagnosticMethod], i.e. the diagnostic-walk thread) and
		// read by StatusMessage / the [Property] getters on the walk thread; an unsynchronized
		// nullable DateTime read can tear. (M17a)
		private readonly object _stateLock = new();
		private bool _isInError;
		private DateTime? _lastError;
		private DateTime? _lastMessageSent;


		public AppenderProxyBase(TimeSpan timeout)
		{
			_timeout = timeout;
		}

		public bool IsInError
		{
			get { lock (_stateLock) return _isInError; }
		}

		[Property]
		public DateTime? LastError
		{
			get { lock (_stateLock) return _lastError; }
			set { lock (_stateLock) _lastError = value; }
		}

		[Property]
		public DateTime? LastMessageSent
		{
			get { lock (_stateLock) return _lastMessageSent; }
			set { lock (_stateLock) _lastMessageSent = value; }
		}

		[RateProperty(ExposeRate = false, ExposeTotal = true)]
		public RateCounter MessagesSent { get; } = new RateCounter(3);

		[RateProperty(ExposeRate = false, ExposeTotal = true)]
		public RateCounter Errors { get; } = new RateCounter(3);

		[DiagnosticMethod]
		public void Reactivate()
		{
			lock (_stateLock)
			{
				_isInError = false;
				_errorTime = null;
			}
		}

		[Property]
		public string LastErrorMessage { get; set; }

		private TimeSpan? TimeUntilNextActive()
		{
			DateTime? time;
			lock (_stateLock)
				time = _errorTime;

			if (!time.HasValue)
				return null;

			TimeSpan elapsed = SystemDateTime.UtcNow() - time.Value;
			if (elapsed > _timeout)
				return null;

			return elapsed;
		}

		[Property]
		public string StatusMessage
		{
			get
			{
				TimeSpan? timeFailed = TimeUntilNextActive();
				if (timeFailed.HasValue)
				{
					string remaining = FormatTimeSpan(_timeout - timeFailed.Value);
					return $"FAILED, Ready in {remaining}";
				}
				return "READY";
			}
		}

		private string FormatTimeSpan(TimeSpan time)
		{
			if (time.TotalMinutes >= 60)
				return string.Format("{0:D2}:{1:D2}:{2:D2}", (int) time.TotalHours, time.Minutes, time.Seconds);

			if (time.TotalSeconds < 60)
				return string.Format("{0} seconds", time.Seconds);

			return string.Format("{0}m {1:D2}s", (int) time.TotalMinutes, time.Seconds);
		}

		protected bool DoAppend(Func<AppendResult> appendAction)
		{
			lock (_stateLock)
			{
				if (ShouldResetErrorNoLock())
				{
					_errorTime = null;
					_isInError = false;
				}

				if (_isInError)
					return false;
			}

			// The append itself runs outside the lock (it can be slow I/O — SMTP send / file write).
			AppendResult result = appendAction();
			if (result.Success)
			{
				LastMessageSent = SystemDateTime.UtcNow();
				MessagesSent.Register(1);
			}
			else
			{
				LastError = SystemDateTime.UtcNow();
				LastErrorMessage = result.Message;
				Errors.Register(1);
				if (_timeout > TimeSpan.Zero)
				{
					// Engage the fail-timeout quarantine. Without this the IsInError guard
					// above never trips, ShouldResetError never runs, and a dead appender is
					// retried on every event ("READY" forever).
					lock (_stateLock)
					{
						_errorTime = SystemDateTime.UtcNow();
						_isInError = true;
					}
				}
			}

			return result.Success;
		}

		// Caller must hold _stateLock.
		private bool ShouldResetErrorNoLock()
		{
			if (!_isInError)
				return false;

			if (_timeout <= TimeSpan.Zero)
				return true;

			// _isInError implies _errorTime was set; guard explicitly so the intent is clear
			// and a future invariant break can't silently rely on nullable arithmetic.
			if (!_errorTime.HasValue)
				return false;

			return (SystemDateTime.UtcNow() - _errorTime.Value) >= _timeout;
		}
	}



	[DiagnosticClass(AttributedPropertiesOnly = true)]
	public class SmtpAppenderProxy : AppenderProxyBase
	{
		private SmtpAppender _appender;

		public SmtpAppenderProxy(SmtpAppender appender, string smtpHost, TimeSpan timeout) : base(timeout)
		{
			_appender = appender;
			SmtpHost = smtpHost;
		}

		public string SmtpHost { get; set; }
		
		public bool TrySend(MailMessage message)
		{
			return DoAppend(() => SendMessage(message));
		}

		private AppendResult SendMessage(MailMessage message)
		{
			try
			{
				using SmtpClient smtpClient = new SmtpClient();
				if (!string.IsNullOrEmpty(SmtpHost) && !string.Equals(SmtpHost, SmtpAppender.DefaultHostName, StringComparison.CurrentCultureIgnoreCase))
					smtpClient.Host = SmtpHost;

				if (_appender.Port > 0)
					smtpClient.Port = _appender.Port;

				smtpClient.DeliveryMethod = SmtpDeliveryMethod.Network;

				// Require TLS when sending Basic-auth credentials so username/password don't traverse
				// the wire in clear; otherwise honour the configured EnableSsl. (M13)
				smtpClient.EnableSsl = _appender.EnableSsl
					|| _appender.Authentication == log4net.Appender.SmtpAppender.SmtpAuthentication.Basic;

				if (_appender.Authentication == log4net.Appender.SmtpAppender.SmtpAuthentication.Basic)
				{
					// Perform basic authentication
					smtpClient.Credentials = new NetworkCredential(_appender.Username, _appender.Password);
				}
				else if (_appender.Authentication == log4net.Appender.SmtpAppender.SmtpAuthentication.Ntlm)
				{
					// Perform integrated authentication (NTLM)
					smtpClient.Credentials = CredentialCache.DefaultNetworkCredentials;
				}
				smtpClient.Send(message);
				return new AppendResult(true);
			}
			catch (Exception ex)
			{
				return new AppendResult(false, ex.Message);
			}
		}
	}

	[DiagnosticClass(AttributedPropertiesOnly = true)]
	public class AppenderProxy : AppenderProxyBase
	{
		public IAppender RawAppender { get; }

		/// <summary>
		/// Wraps up an <see cref="IAppender"/> adding extra behaviour to how to handle
		/// an error while appending
		/// </summary>
		/// <param name="timeout">Duration to wait before attempting to append again after an error</param>
		public AppenderProxy(IAppender appenderToWrap, TimeSpan timeout) : base(timeout)
		{
			RawAppender = appenderToWrap ?? throw new ArgumentNullException(nameof(appenderToWrap));
			AppenderSkeleton convertedAppender = appenderToWrap as AppenderSkeleton;
			if (convertedAppender != null)
			{
				Appender = convertedAppender;
				ErrorHandler = new AppenderProxyErrorHandler();
				MultiErrorHandler.SetErrorHandler(Appender, ErrorHandler);
			}
		}

		private AppenderProxyErrorHandler ErrorHandler { get; }

		/// <summary>
		/// Attempts to append to wrapped appender
		/// </summary>
		/// <returns>Whether the append was successful</returns>
		public bool TryAppend(LoggingEvent loggingEvent)
		{
			return DoAppend(() => FireAppendAction(() => RawAppender.DoAppend(loggingEvent)));
		}

		/// <summary>
		/// Attempts to append to wrapped appender
		/// </summary>
		/// <returns>Whether the append was successful</returns>
		public bool TryAppend(LoggingEvent[] loggingEvents)
		{
			return DoAppend(() => FireAppendAction(() =>
			{
				if (Appender != null)
				{
					Appender.DoAppend(loggingEvents);
				}
				else
				{
					foreach (var loggingEvent in loggingEvents)
					{
						RawAppender.DoAppend(loggingEvent);
					}
				}
			}));
		}

		private AppendResult FireAppendAction(Action appendAction)
		{
			if (ErrorHandler != null)
			{
				ErrorHandler.EnableForCurrentThread();
				ErrorHandler.ResetError();
			}
			try
			{
				appendAction();
			}
			catch (Exception ex)
			{
				return new AppendResult(false, ex.Message);
			}
			finally
			{
				ErrorHandler?.Disable();
			}
			return new AppendResult(ErrorHandler == null || !ErrorHandler.HasError, ErrorHandler?.Message);
		}

		/// <summary>
		/// Appender being wrapped
		/// </summary>
		public AppenderSkeleton Appender { get; }

		public string Name => RawAppender.Name;
	}
}