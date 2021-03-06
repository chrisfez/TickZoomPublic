// <file>
//     <copyright see="prj:///doc/copyright.txt"/>
//     <license see="prj:///doc/license.txt"/>
//     <owner name="Matthew Ward" email="mrward@users.sourceforge.net"/>
//     <version>$Revision: 5343 $</version>
// </file>

using System;
using System.IO;
using System.Reflection;
using System.Xml;

namespace RubyBinding.Tests
{
	/// <summary>
	/// Utility class that reads the RubyBinding.addin file
	/// that has been embedded as a resource into the test assembly.
	/// </summary>
	public sealed class RubyBindingAddInFile
	{
		RubyBindingAddInFile()
		{
		}
		
		/// <summary>
		/// Returns the RubyBinding.addin file.
		/// </summary>
		public static TextReader ReadAddInFile()
		{
			Assembly assembly = Assembly.GetAssembly(typeof(RubyBindingAddInFile));
			string resourceName = String.Concat("RubyBinding.Tests.RubyBinding.addin");
			Stream resourceStream = assembly.GetManifestResourceStream(resourceName);
			if (resourceStream != null) {
				return new StreamReader(resourceStream);
			}
			return null;
		}
	}
}
