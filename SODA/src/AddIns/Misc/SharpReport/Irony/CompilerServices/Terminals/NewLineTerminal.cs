#region License
/* **********************************************************************************
 * Copyright (c) Roman Ivantsov
 * This source code is subject to terms and conditions of the MIT License
 * for Irony. A copy of the license can be found in the License.txt file
 * at the root of this distribution. 
 * By using this source code in any fashion, you are agreeing to be bound by the terms of the 
 * MIT License.
 * You must not remove this notice from this software.
 * **********************************************************************************/
#endregion

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Irony.CompilerServices {
  //This is a simple NewLine terminal recognizing line terminators for use in grammars for line-based languages like VB
  // instead of more complex alternative of using CodeOutlineFilter. 
  public class NewLineTerminal : Terminal {
    public NewLineTerminal(string name) : base(name, TokenCategory.Outline) {
      base.DisplayName = "Line break";
    }

    public string LineTerminators = "\n\r\v";

    #region overrides: Init, GetFirsts, TryMatch
    public override void Init(GrammarData grammarData) {
      base.Init(grammarData);
      //Remove new line chars from whitespace
      foreach(char t in LineTerminators)
        grammarData.Grammar.WhitespaceChars = grammarData.Grammar.WhitespaceChars.Replace(t.ToString(), string.Empty);
    }
    public override IList<string> GetFirsts() {
      StringList firsts = new StringList();
      foreach(char t in LineTerminators)
        firsts.Add(t.ToString());
      return firsts;
    }
    public override Token TryMatch(CompilerContext context, ISourceStream source) {
      char current = source.CurrentChar;
      if (!LineTerminators.Contains(current)) return null;
      //Treat \r\n as a single terminator
      bool doExtraShift = (current == '\r' && source.NextChar == '\n');
      source.Position++; //main shift
      if (doExtraShift)
        source.Position++;
      Token result = new Token(this, source.TokenStart, source.GetLexeme(), null);
      return result;
    }

    #endregion

    
  }//class
}//namespace
