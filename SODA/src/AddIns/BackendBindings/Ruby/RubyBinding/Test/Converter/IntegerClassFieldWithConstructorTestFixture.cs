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
	/// Tests the CSharpToRubyConverter class does not add
	/// an extra constructor method to hold the statements to
	/// initialize the converted class's fields.
	/// </summary>
	[TestFixture]
	public class IntegerClassFieldWithConstructorTestFixture
	{
		string csharp =
			"class Foo\r\n" +
			"{\r\n" +
			"    private int i = 0;\r\n" +
			"    public Foo()\r\n" +
			"    {\r\n" +
			"        int j = 0;\r\n" +
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
				"    def initialize()\r\n" +
				"        @i = 0\r\n" +
				"        j = 0\r\n" +
				"    end\r\n" +
				"end";
			
			Assert.AreEqual(expectedRuby, Ruby);
		}	
	}
}
