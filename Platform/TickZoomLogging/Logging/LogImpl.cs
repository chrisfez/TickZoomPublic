#region Copyright
/*
 * Software: TickZoom Trading Platform
 * Copyright 2009 M. Wayne Walter
 * 
 * This library is free software; you can redistribute it and/or
 * modify it under the terms of the GNU Lesser General Public
 * License as published by the Free Software Foundation; either
 * version 2.1 of the License, or (at your option) any later version.
 * 
 * Business use restricted to 30 days except as otherwise stated in
 * in your Service Level Agreement (SLA).
 * 
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 * 
 * You should have received a copy of the GNU General Public License
 * along with this program; if not, see <http://www.tickzoom.org/wiki/Licenses>
 * or write to Free Software Foundation, Inc., 51 Franklin Street,
 * Fifth Floor, Boston, MA  02110-1301, USA.
 * 
 */
#endregion

using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

using log4net.Core;
using log4net.Filter;
using TickZoom.Api;

namespace TickZoom.Logging
{

	/// <summary>
	/// Description of TickConsole.
	/// </summary>
    [Serializable]
    public class LogImpl : Log
    {
		private readonly static Type callingType = typeof(LogImpl);
		private LogImplWrapper log;
        private string[] indentStrings = new string[50];
		private static Dictionary<string,string> symbolMap;
		private static TimeStamp beginTime;
		private static TimeStamp endTime;
		
		private class LogImplWrapper : log4net.Core.LogImpl {
			private Level m_levelTrace;
			private Level m_levelDebug;
			private Level m_levelInfo;
			private Level m_levelNotice;
			private Level m_levelWarn;
			private Level m_levelError;
			private Level m_levelFatal;
			private bool trace;
			private bool debug;
			private bool info;
			private bool notice;
			private bool warn;
			private bool error;
			private bool fatal;
			private bool isInitialized = false;
			
			public LogImplWrapper(ILogger logger) : base( logger) {
			
			}
			
			private static readonly Level[] levels = new Level[] {
				Level.Trace,
				Level.Debug,
				Level.Info,
				Level.Notice,
				Level.Warn,
				Level.Error,
				Level.Fatal
			};
			
			protected override void ReloadLevels(log4net.Repository.ILoggerRepository repository)
			{
				base.ReloadLevels(repository);
				m_levelTrace = repository.LevelMap.LookupWithDefault(Level.Trace);
				m_levelDebug = repository.LevelMap.LookupWithDefault(Level.Debug);
				m_levelInfo = repository.LevelMap.LookupWithDefault(Level.Info);
				m_levelNotice = repository.LevelMap.LookupWithDefault(Level.Notice);
				m_levelWarn = repository.LevelMap.LookupWithDefault(Level.Warn);
				m_levelError = repository.LevelMap.LookupWithDefault(Level.Error);
				m_levelFatal = repository.LevelMap.LookupWithDefault(Level.Fatal);
				FindAnyEnabled();
				trace = IsAnyEnabledFor(m_levelTrace);
				debug = IsAnyEnabledFor(m_levelDebug);
				info = IsAnyEnabledFor(m_levelInfo);
				notice = IsAnyEnabledFor(m_levelNotice);
				warn = IsAnyEnabledFor(m_levelWarn);
				error = IsAnyEnabledFor(m_levelError);
				fatal = IsAnyEnabledFor(m_levelFatal);
			}
			
			private void TryReloadLevels() {
				if( !isInitialized) {
					ReloadLevels(Logger.Repository);
					isInitialized = true;
				}
			}
			
			public bool IsTraceEnabled {
				get { TryReloadLevels(); return trace; }
			}
			
			public override bool IsDebugEnabled {
				get { TryReloadLevels(); return debug; }
			}
			
			public override bool IsInfoEnabled {
				get { TryReloadLevels(); return info; }
			}
			
			public bool IsNoticeEnabled {
				get { TryReloadLevels(); return notice; }
			}
			
			public override bool IsWarnEnabled {
				get { TryReloadLevels(); return warn; }
			}
			
			public override bool IsErrorEnabled {
				get { TryReloadLevels(); return error; }
			}
			
			public override bool IsFatalEnabled {
				get { TryReloadLevels(); return fatal; }
			}
			
			private bool IsAnyEnabledFor(Level level) {
				while( true) {
					try {
						return CheckAnyEnabled(level);
					} catch( InvalidOperationException) {
					}
				}
			}

			private Dictionary<Level,int> childrenByLevel;
			
			private void FindAnyEnabled() {
				childrenByLevel = new Dictionary<Level,int>();
				foreach( var level in levels) {
					childrenByLevel[level] = 0;
				}
				ILogger[] loggers = null;
				while( loggers == null) {
					try {
						loggers = Logger.Repository.GetCurrentLoggers();
					} catch( InvalidOperationException) {
						
					}
				}
				for( var i=0; i<loggers.Length; i++) {
					var child = loggers[i];
					foreach( var level in levels) {
						if( child.IsEnabledFor(level)) {
							if( child.Name.StartsWith(Logger.Name)) {
						        childrenByLevel[level]++;
							} else if( child.Name.Contains("*") || child.Name.Contains("?")) {
								var wildcard = new Wildcard(child.Name, RegexOptions.IgnoreCase);
								if( wildcard.IsMatch(Logger.Name)) {
									childrenByLevel[level]++;
								}
							}
						}
					}
				}
			}
			
			private bool CheckAnyEnabled(Level level) {
				return childrenByLevel[level] > 0;
			}
		}
		
#region OldStuff
        private static LoggingQueue messageQueue = null; 
        private static object locker = new object();
        private string fileName;
		
        public LogImpl(ILogger logger) {
			log = new LogImplWrapper(logger);
        	for( int i=0; i<50; i++) {
        		indentStrings[i] = new string(' ',i);
        	}
        	Connect();
			if( symbolMap == null) {
				lock( locker) {
        			if( symbolMap == null) {
        				ConvertSymbols();
        				ConvertTimes();
        			}
        		}
			}
       	}
        
 		private void ConvertSymbols() {
			symbolMap = new Dictionary<string, string>();
			string symbols = Factory.Settings["LogSymbols"];
			if( symbols != null) {
				string[] array = symbols.Split(',');
				for( int i=0;i<array.Length; i++) {
					string symbol = array[i].Trim();
					if( symbol.Length>0) {
						symbolMap[symbol] = null;
					}
				}
			}
		}
		
		private void ConvertTimes() {
			beginTime = TimeStamp.MinValue;
			string beginTimeStr = Factory.Settings["LogTickStart"];
			if( beginTimeStr != null) {
				beginTimeStr = beginTimeStr.Trim();
				if( beginTimeStr.Length > 0) {
					beginTime = new TimeStamp(beginTimeStr);
				}
			}
			endTime = TimeStamp.MaxValue;
			string endTimeStr = Factory.Settings["LogTickStop"];
			if( endTimeStr != null) {
				endTimeStr = endTimeStr.Trim();
				if( endTimeStr.Length > 0) {
					endTime = new TimeStamp(endTimeStr);
				}
			}
		}
		
		private void Connect() {
        	if( messageQueue == null) {
        		lock( locker) {
		        	if( messageQueue == null) {
        				messageQueue = new LoggingQueue();
	       			}
        		}
        	}
        }

        public Log LogFile(string fileName) {
        	throw new NotImplementedException();
	    }
        
        public void WriteLine(string msg)
        {
        	Info(msg);
        }
        
        private void WriteScreen(LoggingEvent msg) {
        	if( messageQueue != null) {
        		messageQueue.EnQueue(msg);
    	    }
        }
        
        public void Clear() {
        }
        
        public void WriteFile(string msg)
        {
      		Debug(msg);
        }
        
        public bool HasLine {
        	get {
        		return messageQueue.Count > 0;
        	}
        }
        public bool TryReadLine(out LogEvent logEvent) {
        	if( messageQueue == null) {
        		throw new ApplicationException( "Sorry. You must Connect before ReadLine");
        	}
        	LoggingEvent msg = default(LoggingEvent);
        	if( messageQueue.Dequeue(out msg)) {
	        	logEvent = new LogEventDefault() {
	        		IsAudioAlarm = msg.Level >= Level.Error,
					MessageObject = msg.RenderedMessage,
	        	};
        		return true;
        	} else {
        		logEvent = null;
        		return false;
        	}
        }
        
        int indent = 0;
        
        public void Indent() {
        	indent += 2;
        	AdjustIndentString();
        }
        
        public void Outdent() {
        	indent -= 2;
        	AdjustIndentString();
        }

        private void AdjustIndentString() {
       		if( indent < 0) {
        		indent = 0;
        	}
        }
        
 		public string FileName {
			get { return fileName; }
			set { fileName = value; }
		}
        
#endregion
		public TimeStamp TimeStamp {
			get { return new TimeStamp(log4net.MDC.Get("TimeStamp")); }
			set { log4net.MDC.Set("TimeStamp",value.ToString()); }
		}

		public string Symbol {
			get { return log4net.MDC.Get("Symbol"); }
			set { log4net.MDC.Set("Symbol",value); }
		}

		public int CurrentBar {
			get { int bar;
				if( int.TryParse(log4net.MDC.Get("CurrentBar"),out bar)) {
					return bar;
				}else {
					throw new FormatException("Unable to parse " + log4net.MDC.Get("CurrentBar") + " as in int.");
				}
			}
			set { log4net.MDC.Set("CurrentBar",value.ToString()); }
		}
		
		public bool IsNoticeEnabled {
			get { return log.IsNoticeEnabled; }
		}
		
		public bool IsTraceEnabled {
			get { return log.IsTraceEnabled; }
		}
		
		public bool IsDebugEnabled {
			get { return log.IsDebugEnabled; }
		}
		
		public bool IsInfoEnabled {
			get { return log.IsInfoEnabled; }
		}
		
		public bool IsWarnEnabled {
			get { return log.IsWarnEnabled; }
		}
		
		public bool IsErrorEnabled {
			get { return log.IsErrorEnabled; }
		}
		
		public bool IsFatalEnabled {
			get { return log.IsFatalEnabled; }
		}
		
		public void Assert(bool test) {
			if( test == false) {
				Exception ex = new AssertFailedException(new StackTrace(1,true));
				log.Error("Assertion Failed", ex);
				throw ex;
			}
		}
		
		public void Notice(object message)
      	{
			Notice(message,null);
		}
		
		public void Trace(object message)
      	{
			Trace(message,null);
		}
		
		public void Debug(object message)
      	{
			Debug(message,null);
		}
		
		public void Info(object message)
		{
			Info(message,null);
        }
        
		public void Warn(object message)
		{
			Warn(message,null);
		}
        
		public void Error(object message)
		{
			Error(message,null);
		}
		
		public void Fatal(object message)
		{
			Fatal(message,null);
		}
		
		public void Notice(object message, Exception t)
		{
			if (IsNoticeEnabled)
			{
				var data = BuildEventData(log.Logger.Name, Level.Notice, message, t);
				var loggingEvent = new LoggingEvent( callingType, log.Logger.Repository, data);
				if( t!=null) {
					System.Diagnostics.Debug.WriteLine(message + "\n" + t);
				}
				SetProperties(loggingEvent);
        		WriteScreen(loggingEvent);
				log.Logger.Log(loggingEvent);
			}
		}
		
		public void Trace(object message, Exception t)
		{
			if (IsTraceEnabled)
			{
				var data = BuildEventData(log.Logger.Name, Level.Trace, message, t);
				var loggingEvent = new LoggingEvent( callingType, log.Logger.Repository, data);
				if( t!=null) {
					System.Diagnostics.Debug.WriteLine(message + "\n" + t);
				}
				SetProperties(loggingEvent);
				log.Logger.Log(loggingEvent);
			}
		}
		
		public void Call() {
			if (IsDebugEnabled)
			{
				StackTrace trace = new StackTrace();
				MethodBase callee = trace.GetFrame(1).GetMethod();
				Type calleeObj = callee.DeclaringType;
				MethodBase caller = trace.GetFrame(2).GetMethod();
				Type callerObj = caller.DeclaringType;
				if( callee.Name == ".ctor") {
					Debug(GetTypeName(callerObj) + " (!) " + GetTypeName(calleeObj) );
				} else {
					Debug(GetTypeName(callerObj) + " ==> " + GetTypeName(calleeObj) + " " + GetSignature(callee));
				}
			}
		}
		
		public void Async(Action action) {
			if (IsDebugEnabled)
			{
				StackTrace trace = new StackTrace();
				MethodBase caller = trace.GetFrame(1).GetMethod();
				Type callerObj = caller.DeclaringType;
				Delegate[] del = action.GetInvocationList();
				MethodInfo callee = del[0].Method;
				Type calleeObj = callee.DeclaringType;
				Debug(GetTypeName(callerObj) + " >-- " + GetTypeName(calleeObj) + " " + GetSignature(callee));
				Debug(GetTypeName(callerObj) + " --> " + GetTypeName(calleeObj) + " " + GetSignature(callee));
			}
		}
		
		public void Return() {
			if (IsDebugEnabled)
			{
				StackTrace trace = new StackTrace();
				MethodBase callee = trace.GetFrame(1).GetMethod();
				Type calleeObj = callee.DeclaringType;
				MethodBase caller = trace.GetFrame(2).GetMethod();
				Type callerObj = caller.DeclaringType;
				Debug(GetTypeName(callerObj) + " <== " + GetTypeName(calleeObj) + " " + GetSignature(callee));
			}
		}
		
		private string StripTilda(string typeName) {
			return typeName.Substring(0,typeName.IndexOf('`'));
		}
		
		private string GetSignature(MethodBase method) {
			ParameterInfo[] parameters = method.GetParameters();
			StringBuilder builder = new StringBuilder();
			builder.Append(method.Name);
			builder.Append("(");
			for( int i=0; i<parameters.Length; i++) {
				if( i!=0) builder.Append(",");
				ParameterInfo parameter = parameters[i];
				Type type = parameter.ParameterType;
				builder.Append(GetTypeName(type));
			}
			builder.Append(")");
			return builder.ToString();
		}

		private string GetTypeName(Type type) {
			Type[] generics = type.GetGenericArguments();
			if( generics.Length>0) {
				StringBuilder builder = new StringBuilder();
				builder.Append(StripTilda(type.Name));
				builder.Append("<");
				for( int j=0; j<generics.Length; j++) {
					if( j!=0) builder.Append(",");
					Type generic = generics[j];
					builder.Append(generic.Name);
				}
				builder.Append(">");
				return builder.ToString();
			} else {
				return type.Name;
			}
		}
		public void Debug(object message, Exception t)
		{
			if (IsDebugEnabled)
			{
				var data = BuildEventData(log.Logger.Name, Level.Debug, message, t);
				var loggingEvent = new LoggingEvent( callingType, log.Logger.Repository, data);
				if( t!=null) {
					System.Diagnostics.Debug.WriteLine(message + "\n" + t);
				}
				SetProperties(loggingEvent);
				log.Logger.Log(loggingEvent);
			}
		}
		
		public void Info(object message, Exception t)
		{
			if (IsInfoEnabled)
			{
				var data = BuildEventData(log.Logger.Name, Level.Info, message, t);
				var loggingEvent = new LoggingEvent( callingType, log.Logger.Repository, data);
				if( t!=null) {
					System.Diagnostics.Debug.WriteLine(message + "\n" + t);
				}
				SetProperties(loggingEvent);
				log.Logger.Log(loggingEvent);
			}
		}
		
		private LoggingEventData BuildEventData(string logger, Level level, object message, Exception exception)
		{
			string exceptionString = null;
			if( exception != null) {
				exceptionString = exception.ToString();
			}
			var data = new LoggingEventData() {
				LoggerName = logger,
				Level = level, 
				Message = message.ToString(),
				ThreadName = Thread.CurrentThread.Name,
				TimeStamp = TimeStamp.UtcNow.DateTime,
				ExceptionString = exceptionString,
			};
			return data;
		}
		
		public void Warn(object message, Exception t)
		{
			if (IsWarnEnabled)
			{
				var data = BuildEventData(log.Logger.Name, Level.Warn, message, t);
				var loggingEvent = new LoggingEvent( callingType, log.Logger.Repository, data);
				if( t!=null) {
					System.Diagnostics.Debug.WriteLine(message + "\n" + t);
				}
				SetProperties(loggingEvent);
	        	WriteScreen(loggingEvent);
				log.Logger.Log(loggingEvent);
			}
		}
		
		public void Error(object message, Exception t)
		{
			if (IsErrorEnabled)
			{
				var data = BuildEventData(log.Logger.Name, Level.Error, message, t);
				var loggingEvent = new LoggingEvent( callingType, log.Logger.Repository, data);
				if( t!=null) {
					System.Diagnostics.Debug.WriteLine(message + "\n" + t);
				}
				SetProperties(loggingEvent);
	        	WriteScreen(loggingEvent);
				log.Logger.Log(loggingEvent);
			}
		}
		
		private FilterDecision SymbolDecide()
		{
			string symbol = log4net.MDC.Get("Symbol");
			if( symbol != null && symbol.Length>0 && symbolMap.Count > 0) {
				if( symbolMap.ContainsKey(symbol)) {
					return FilterDecision.Neutral;
				}
				else
				{
					return FilterDecision.Deny;
				}
			} else {
				return FilterDecision.Accept;
			}
		}
		
		private FilterDecision TimeStampDecide()
		{
			string timeStampStr = log4net.MDC.Get("TimeStamp");
			if( timeStampStr != null && timeStampStr.Length>0) {
				TimeStamp timeStamp = new TimeStamp(timeStampStr);
				if( timeStamp >= beginTime && timeStamp <= endTime) {
					return FilterDecision.Neutral;
				}
				else
				{
					return FilterDecision.Deny;
				}
			} else {
				return FilterDecision.Accept;
			}
		}

		private void SetProperties(LoggingEvent loggingEvent) {
//			loggingEvent.Properties["TimeStamp"] = CheckNull(log4net.MDC.Get("TimeStamp");
//			loggingEvent.Properties["Symbol"] = CheckNull(log4net.MDC.Get("Symbol"));
//			loggingEvent.Properties["CurrentBar"] = CheckNull(log4net.MDC.Get("CurrentBar"));
		}
		
		private string CheckNull(string value) {
			if( value == null) {
				return "";
			} else {
				return value;
			}
		}
		
		public void Fatal(object message, Exception t)
		{
			if (IsFatalEnabled)
			{
				LoggingEvent loggingEvent = new LoggingEvent(callingType, log.Logger.Repository, log.Logger.Name, Level.Fatal, message, t);
				SetProperties(loggingEvent);
	        	WriteScreen(loggingEvent);
				log.Logger.Log(loggingEvent);
			}
		}
		
		public void TraceFormat(string format, params object[] args)
		{
			
			Trace(string.Format(format,args));
		}
		
		public void DebugFormat(string format, params object[] args)
		{
			log.DebugFormat(format, args);
		}
		
		public void InfoFormat(string format, params object[] args)
		{
			log.InfoFormat(format, args);
		}
		
		public void NoticeFormat(string format, params object[] args)
		{
			Notice(string.Format(format,args));
		}
		
		public void WarnFormat(string format, params object[] args)
		{
			log.WarnFormat(format, args);
		}
		
		public void ErrorFormat(string format, params object[] args)
		{
			log.ErrorFormat(format, args);
		}
		
		public void FatalFormat(string format, params object[] args)
		{
			log.FatalFormat(format, args);
		}
		
		public void TraceFormat(IFormatProvider provider, string format, params object[] args)
		{
			Trace(string.Format(provider, format, args));
		}
		
		public void DebugFormat(IFormatProvider provider, string format, params object[] args)
		{
			log.DebugFormat(provider, format, args);
		}
		
		public void InfoFormat(IFormatProvider provider, string format, params object[] args)
		{
			log.InfoFormat(provider, format, args);
		}
		
		public void NoticeFormat(IFormatProvider provider, string format, params object[] args)
		{
			Notice(string.Format(provider, format, args));
		}
		
		public void WarnFormat(IFormatProvider provider, string format, params object[] args)
		{
			log.WarnFormat(provider, format, args);
		}
		
		public void ErrorFormat(IFormatProvider provider, string format, params object[] args)
		{
			log.ErrorFormat(provider, format, args);
		}
		
		public void FatalFormat(IFormatProvider provider, string format, params object[] args)
		{
			log.FatalFormat(provider, format, args);
		}
	}
}
