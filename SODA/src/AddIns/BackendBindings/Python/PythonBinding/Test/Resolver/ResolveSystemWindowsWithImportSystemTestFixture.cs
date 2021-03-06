// <file>
//     <copyright see="prj:///doc/copyright.txt"/>
//     <license see="prj:///doc/license.txt"/>
//     <owner name="Matthew Ward" email="mrward@users.sourceforge.net"/>
//     <version>$Revision: 5476 $</version>
// </file>

using System;
using System.Collections;
using ICSharpCode.PythonBinding;
using ICSharpCode.SharpDevelop.Dom;
using NUnit.Framework;
using PythonBinding.Tests;
using PythonBinding.Tests.Utils;

namespace PythonBinding.Tests.Resolver
{
	[TestFixture]
	public class ResolveSystemWindowsWithImportSystemTestFixture : ResolveTestFixtureBase
	{
		protected override ExpressionResult GetExpressionResult()
		{
			MockProjectContent referencedContent = new MockProjectContent();
			referencedContent.AddExistingNamespaceContents("System.Windows.Forms", new ArrayList());
			projectContent.ReferencedContents.Add(referencedContent);
			
			return new ExpressionResult("System.Windows");
		}
		
		protected override string GetPythonScript()
		{
			return
				"import System\r\n" +
				"System.Windows\r\n";
		}
		
		NamespaceResolveResult NamespaceResolveResult {
			get { return resolveResult as NamespaceResolveResult; }
		}
		
		[Test]
		public void NamespaceResolveResultHasSystemWindowsNamespace()
		{
			Assert.AreEqual("System.Windows", NamespaceResolveResult.Name);
		}
	}
}
