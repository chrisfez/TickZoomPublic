// <file>
//     <copyright see="prj:///doc/copyright.txt"/>
//     <license see="prj:///doc/license.txt"/>
//     <owner name="Matthew Ward" email="mrward@users.sourceforge.net"/>
//     <version>$Revision: 5343 $</version>
// </file>

using System;
using ICSharpCode.NRefactory;
using ICSharpCode.RubyBinding;
using NUnit.Framework;

namespace RubyBinding.Tests.Converter
{
	/// <summary>
	/// Tests that the keyword this is used when an explicit reference to a field is used.
	/// Also tests that any constructor parameters are generated.
	/// </summary>
	[TestFixture]	
	public class ClassFieldReferenceConversionTestFixture
	{		
		string csharp = "class Foo\r\n" +
						"{\r\n" +
						"    int i;\r\n" +
						"    int j;\r\n" +
						"    public Foo(int i)\r\n" +
						"    {\r\n" +
						"        this.i = i;\r\n" +
						"    }\r\n" +
						"\r\n" +
						"    public void SetInt(int j)\r\n" +
						"    {\r\n" +
						"        this.j = j;\r\n" +
						"    }\r\n" +
						"}";
				
		[Test]
		public void ConvertedRubyCode()
		{
			NRefactoryToRubyConverter converter = new NRefactoryToRubyConverter(SupportedLanguage.CSharp);
			converter.IndentString = "    ";
			string Ruby = converter.Convert(csharp);
			string expectedRuby = 
				"class Foo\r\n" +
				"    def initialize(i)\r\n" +
				"        self.@i = i\r\n" +
				"    end\r\n" +
				"\r\n" +
				"    def SetInt(j)\r\n" +
				"        self.@j = j\r\n" +
				"    end\r\n" +
				"end";
			
			Assert.AreEqual(expectedRuby, Ruby, Ruby);
		}
	}
}

