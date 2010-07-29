// <file>
//     <copyright see="prj:///doc/copyright.txt"/>
//     <license see="prj:///doc/license.txt"/>
//     <owner name="David Srbecký" email="dsrbecky@gmail.com"/>
//     <version>$Revision: 2201 $</version>
// </file>

// This file is automatically generated - any changes will be lost

#pragma warning disable 1591

namespace Debugger.Wrappers.CorDebug
{
	using System;
	
	
	public partial class ICorDebugGenericValue
	{
		
		private Debugger.Interop.CorDebug.ICorDebugGenericValue wrappedObject;
		
		internal Debugger.Interop.CorDebug.ICorDebugGenericValue WrappedObject
		{
			get
			{
				return this.wrappedObject;
			}
		}
		
		public ICorDebugGenericValue(Debugger.Interop.CorDebug.ICorDebugGenericValue wrappedObject)
		{
			this.wrappedObject = wrappedObject;
			ResourceManager.TrackCOMObject(wrappedObject, typeof(ICorDebugGenericValue));
		}
		
		public static ICorDebugGenericValue Wrap(Debugger.Interop.CorDebug.ICorDebugGenericValue objectToWrap)
		{
			if ((objectToWrap != null))
			{
				return new ICorDebugGenericValue(objectToWrap);
			} else
			{
				return null;
			}
		}
		
		~ICorDebugGenericValue()
		{
			object o = wrappedObject;
			wrappedObject = null;
			ResourceManager.ReleaseCOMObject(o, typeof(ICorDebugGenericValue));
		}
		
		public bool Is<T>() where T: class
		{
			System.Reflection.ConstructorInfo ctor = typeof(T).GetConstructors()[0];
			System.Type paramType = ctor.GetParameters()[0].ParameterType;
			return paramType.IsInstanceOfType(this.WrappedObject);
		}
		
		public T As<T>() where T: class
		{
			try {
				return CastTo<T>();
			} catch {
				return null;
			}
		}
		
		public T CastTo<T>() where T: class
		{
			return (T)Activator.CreateInstance(typeof(T), this.WrappedObject);
		}
		
		public static bool operator ==(ICorDebugGenericValue o1, ICorDebugGenericValue o2)
		{
			return ((object)o1 == null && (object)o2 == null) ||
			       ((object)o1 != null && (object)o2 != null && o1.WrappedObject == o2.WrappedObject);
		}
		
		public static bool operator !=(ICorDebugGenericValue o1, ICorDebugGenericValue o2)
		{
			return !(o1 == o2);
		}
		
		public override int GetHashCode()
		{
			return base.GetHashCode();
		}
		
		public override bool Equals(object o)
		{
			ICorDebugGenericValue casted = o as ICorDebugGenericValue;
			return (casted != null) && (casted.WrappedObject == wrappedObject);
		}
		
		
		public uint Type
		{
			get
			{
				uint pType;
				this.WrappedObject.GetType(out pType);
				return pType;
			}
		}
		
		public uint Size
		{
			get
			{
				uint pSize;
				this.WrappedObject.GetSize(out pSize);
				return pSize;
			}
		}
		
		public ulong Address
		{
			get
			{
				ulong pAddress;
				this.WrappedObject.GetAddress(out pAddress);
				return pAddress;
			}
		}
		
		public ICorDebugValueBreakpoint CreateBreakpoint()
		{
			ICorDebugValueBreakpoint ppBreakpoint;
			Debugger.Interop.CorDebug.ICorDebugValueBreakpoint out_ppBreakpoint;
			this.WrappedObject.CreateBreakpoint(out out_ppBreakpoint);
			ppBreakpoint = ICorDebugValueBreakpoint.Wrap(out_ppBreakpoint);
			return ppBreakpoint;
		}
		
		public void GetValue(System.IntPtr pTo)
		{
			this.WrappedObject.GetValue(pTo);
		}
		
		public void SetValue(System.IntPtr pFrom)
		{
			this.WrappedObject.SetValue(pFrom);
		}
	}
}

#pragma warning restore 1591