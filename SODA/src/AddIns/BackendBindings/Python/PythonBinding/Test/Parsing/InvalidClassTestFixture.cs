// <file>
//     <copyright see="prj:///doc/copyright.txt"/>
//     <license see="prj:///doc/license.txt"/>
//     <owner name="Matthew Ward" email="mrward@users.sourceforge.net"/>
//     <version>$Revision: 2753 $</version>
// </file>

using System;
using ICSharpCode.PythonBinding;
using ICSharpCode.SharpDevelop.Dom;
using NUnit.Framework;
using PythonBinding.Tests;

namespace PythonBinding.Tests.Parsing
{
	/// <summary>
	/// Tests that if one part of a class is invalid the parser
	/// still returns a class as part of the compilation unit. 
	/// This is better than returning nothing.
	/// </summary>
	[TestFixture]
	public class InvalidClassTestFixture
	{
		ICompilationUnit compilationUnit;
		
		[TestFixtureSetUp]
		public void SetUpFixture()
		{
			string python = "import System\r\n" +
							"\r\n" +
							"class Class1:\r\n" +
							"	def __init__(self):\r\n" +
							"		Console.\r\n" +
							"		pass\r\n";

			DefaultProjectContent projectContent = new DefaultProjectContent();
			PythonParser parser = new PythonParser();
			compilationUnit = parser.Parse(projectContent, @"C:\test.py", python);			
		}
		
		[Test]
		public void OneClassFound()
		{
			Assert.AreEqual(1, compilationUnit.Classes.Count);
		}
				
		[Test]
		public void FileNameSet()
		{
			Assert.AreEqual(@"C:\test.py", compilationUnit.FileName);
		}
	}
}
