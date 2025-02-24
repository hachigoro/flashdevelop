using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text.RegularExpressions;
using ASCompletion.Context;
using ASCompletion.Model;
using PluginCore;
using PluginCore.Controls;
using PluginCore.Localization;
using PluginCore.Managers;
using PluginCore.Utilities;
using ScintillaNet;

namespace ASCompletion.Completion
{
    public class CommentBlock
    {
        public string Description;
        public string InfoTip;
        public string Return;
        public bool IsFunctionWithArguments;
        public List<string> ParamName;
        public List<string> ParamDesc;
        public List<string> TagName;
        public List<string> TagDesc;
    }
    
    public class ASDocumentation
    {
        static List<ICompletionListItem> docVariables;
        
        #region regular_expressions

        static readonly Regex re_tags = new Regex("<[/]?(p|br)[/]?>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        #endregion
        
        #region Comment generation
        public static bool OnChar(ScintillaControl sci, int value, int position, int style)
        {
            if (style == 3 || style == 124)
            {
                switch (value)
                {
                    // documentation tag
                    case '@': return HandleDocTagCompletion(sci);
                    
                    // documentation bloc
                    case '*': return ASContext.Context.DocumentationGenerator.ContextualGenerator(sci, position, new List<ICompletionListItem>());
                }
            }
            return false;
        }

        static bool HandleDocTagCompletion(ScintillaControl sci)
        {
            if (ASContext.CommonSettings.JavadocTags.IsNullOrEmpty()) return false;

            string txt = sci.GetLine(sci.CurrentLine).TrimStart();
            if (!Regex.IsMatch(txt, "^\\*[\\s]*\\@"))
                return false;
            
            // build tag list
            if (docVariables is null)
            {
                docVariables = new List<ICompletionListItem>();
                foreach (string tag in ASContext.CommonSettings.JavadocTags)
                {
                    docVariables.Add(new TagItem(tag));
                }               
            }
            
            // show
            CompletionList.Show(docVariables, true, "");
            return true;
        }
        
        /// <summary>
        /// Documentation tag template completion list item
        /// </summary>
        class TagItem : ICompletionListItem
        {
            readonly string label;
            
            public TagItem(string label) 
            {
                this.label = label;
            }
            
            public string Label => label;

            public string Description => TextHelper.GetString("Label.DocTagTemplate");

            public Bitmap Icon => (Bitmap)ASContext.Panel.GetIcon(PluginUI.ICON_DECLARATION);

            public string Value => label;
        }
        #endregion
        
        #region Tooltips

        static readonly Regex reNewLine = new Regex("[\r\n]+", RegexOptions.Compiled);
        static readonly Regex reKeepTags = new Regex("<([/]?(b|i|s|u))>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        static readonly Regex reSpecialTags = new Regex("<([/]?)(code|small|strong|em)>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        static readonly Regex reStripTags = new Regex("<[/]?[a-z]+[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        static readonly Regex reDocTags = new Regex("\n@(?<tag>[a-z]+)\\s", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        static readonly Regex reSplitParams = new Regex("(?<var>[\\w$]+)\\s", RegexOptions.Compiled);

        public static CommentBlock ParseComment(string comment)
        {
            // cleanup
            comment = comment.Replace("&lt;", "<").Replace("&gt;", ">").Replace("&nbsp;", " ");
            comment = reKeepTags.Replace(comment, "[$1]");
            comment = reSpecialTags.Replace(comment, match =>
            {
                string tag = match.Groups[2].Value;
                bool open = match.Groups[1].Length == 0;
                return tag switch
                {
                    "small" => open ? "[size=-2]" : "[/size]",
                    "code" => open ? "[font=Courier New]" : "[/font]",
                    "strong" => open ? "[b]" : "[/b]",
                    "em" => open ? "[i]" : "[/i]",
                    _ => "",
                };
            });
            comment = reStripTags.Replace(comment, "");
            string[] lines = reNewLine.Split(comment);
            char[] trim = { ' ', '\t', '*' };
            bool addNL = false;
            comment = "";
            foreach (string line in lines)
            {
                string temp = line.Trim(trim);
                if (addNL) comment += '\n' + temp;
                else
                {
                    comment += temp;
                    addNL = true;
                }
            }
            // extraction
            CommentBlock cb = new CommentBlock();
            MatchCollection tags = reDocTags.Matches(comment);
            
            if (tags.Count == 0)
            {
                cb.Description = comment.Trim();
                return cb;
            }
            
            cb.Description = tags[0].Index > 0
                ? comment.Substring(0, tags[0].Index).Trim()
                : string.Empty;
            cb.TagName = new List<string>();
            cb.TagDesc = new List<string>();

            for(int i = 0; i < tags.Count; i++)
            {
                var gTag = tags[i].Groups["tag"];
                string tag = gTag.Value;
                int start = gTag.Index+gTag.Length;
                int end = (i<tags.Count-1) ? tags[i+1].Index : comment.Length;
                string desc = comment.Substring(start, end-start).Trim();
                if (tag == "param")
                {
                    Match mParam = reSplitParams.Match(desc);
                    if (mParam.Success)
                    {
                        Group mVar = mParam.Groups["var"];
                        if (cb.ParamName is null) {
                            cb.ParamName = new List<string>();
                            cb.ParamDesc = new List<string>();
                        }
                        cb.ParamName.Add(mVar.Value);
                        cb.ParamDesc.Add(desc.Substring(mVar.Index + mVar.Length).TrimStart());
                    }
                }
                else if (tag == "return")
                {
                    cb.Return = desc;
                }
                else if (tag == "infotip")
                {
                    cb.InfoTip = desc;
                    if (cb.Description.Length == 0) cb.Description = cb.InfoTip;
                }
                cb.TagName.Add(tag);
                cb.TagDesc.Add(desc);
            }
            return cb;
            
        }
        
        public static string GetTipDetails(MemberModel member, string highlightParam)
        {
            try
            {
                string tip = (UITools.Manager.ShowDetails) ? GetTipFullDetails(member, highlightParam) : GetTipShortDetails(member, highlightParam);
                // remove paragraphs from comments
                return RemoveHTMLTags(tip);
            }
            catch(Exception ex)
            {
                ErrorManager.ShowError(/*"Error while parsing comments.\n"+ex.Message,*/ ex);
                return "";
            }
        }

        public static string RemoveHTMLTags(string tip) => re_tags.Replace(tip, "");

        /// <summary>
        /// Short contextual details to display in tips
        /// </summary>
        /// <param name="member">Member data</param>
        /// <param name="highlightParam">Parameter to detail</param>
        /// <returns></returns>
        public static string GetTipShortDetails(MemberModel member, string highlightParam)
        {
            if (member?.Comments is null || !ASContext.CommonSettings.SmartTipsEnabled) return "";
            CommentBlock cb = ParseComment(member.Comments);
            cb.IsFunctionWithArguments = IsFunctionWithArguments(member);
            return " \u2026" + GetTipShortDetails(cb, highlightParam);
        }

        static bool IsFunctionWithArguments(MemberModel member)
        {
            return member != null && (member.Flags & FlagType.Function) > 0
                && !member.Parameters.IsNullOrEmpty();
        }

        /// <summary>
        /// Short contextual details to display in tips
        /// </summary>
        /// <param name="cb">Parsed comments</param>
        /// <returns>Formated comments</returns>
        public static string GetTipShortDetails(CommentBlock cb, string highlightParam)
        {
            string details = "";
            
            // get parameter detail
            if (!string.IsNullOrEmpty(highlightParam) && cb.ParamName != null)
            {
                for(int i=0; i<cb.ParamName.Count; i++)
                {
                    if (highlightParam == cb.ParamName[i])
                    {
                        details += "\n" + MethodCallTip.HLTextStyleBeg + highlightParam + ":" + MethodCallTip.HLTextStyleEnd 
                                + " " + Get2LinesOf(cb.ParamDesc[i], true).TrimStart();
                        return details;
                    }
                }
            }
            // get description extract
            if (ASContext.CommonSettings.SmartTipsEnabled)
            {
                if (!string.IsNullOrEmpty(cb.InfoTip))
                    details += "\n"+cb.InfoTip;
                else if (!string.IsNullOrEmpty(cb.Description)) 
                    details += Get2LinesOf(cb.Description, cb.IsFunctionWithArguments);
            }

            return details;
        }

        static string GetShortcutDocs()
        {
            Color themeForeColor = PluginBase.MainForm.GetThemeColor("MethodCallTip.InfoColor");
            string foreColorString = themeForeColor != Color.Empty ? DataConverter.ColorToHex(themeForeColor).Replace("0x", "#") : "#666666:MULTIPLY";
            return "\n[COLOR=" + foreColorString + "][i](" + TextHelper.GetString("Info.ShowDetails") + ")[/i][/COLOR]";
        }

        /// <summary>
        /// Split multiline text and return 2 lines or less of text
        /// </summary>
        public static string Get2LinesOf(string text) => Get2LinesOf(text, false);

        public static string Get2LinesOf(string text, bool alwaysAddShortcutDocs)
        {
            string[] lines = text.Split('\n');
            text = "";
            int n = Math.Min(lines.Length, 2);
            for (int i = 0; i < n; i++) text += "\n" + lines[i];
            if (lines.Length > 2 || alwaysAddShortcutDocs) text += " \x86" + GetShortcutDocs();
            return text;
        }
        
        /// <summary>
        /// Extract member comments for display in the completion list
        /// </summary>
        /// <param name="member">Member data</param>
        /// <param name="highlightParam">Parameter to highlight</param>
        /// <returns>Formatted comments</returns>
        public static string GetTipFullDetails(MemberModel member, string highlightParam)
        {
            if (member?.Comments is null || !ASContext.CommonSettings.SmartTipsEnabled) return "";
            CommentBlock cb = ParseComment(member.Comments);
            cb.IsFunctionWithArguments = IsFunctionWithArguments(member);
            return GetTipFullDetails(cb, highlightParam);
        }

        /// <summary>
        /// Extract comments for display in the completion list
        /// </summary>
        /// <param name="cb">Parsed comments</param>
        /// <returns>Formated comments</returns>
        public static string GetTipFullDetails(CommentBlock cb, string highlightParam)
        {
            string details = "";
            if (cb.Description.Length > 0) 
            {
                string[] lines = cb.Description.Split('\n');
                int n = Math.Min(lines.Length, ASContext.CommonSettings.DescriptionLinesLimit);
                for (int i = 0; i < n; i++) details += lines[i] + "\n";
                if (lines.Length > ASContext.CommonSettings.DescriptionLinesLimit) details = details.TrimEnd() + " \u2026\n";
            }
            
            // @usage
            if (cb.TagName != null)
            {
                bool hasUsage = false;
                for(int i=0; i<cb.TagName.Count; i++)
                    if (cb.TagName[i] == "usage") 
                    {
                        hasUsage = true;
                        details += "\n    "+cb.TagDesc[i];
                    }
                if (hasUsage) details += "\n";
            }
            
            // @param
            if (!cb.ParamName.IsNullOrEmpty())
            {
                details += "\nParam:";
                for(int i=0; i<cb.ParamName.Count; i++)
                {
                    details += "\n    ";
                    if (highlightParam == cb.ParamName[i])
                    {
                        details += MethodCallTip.HLBgStyleBeg 
                                + MethodCallTip.HLTextStyleBeg + highlightParam + ":" + MethodCallTip.HLTextStyleEnd + " "
                                + cb.ParamDesc[i] 
                                + MethodCallTip.HLBgStyleEnd;
                    }
                    else details += cb.ParamName[i] + ": " + cb.ParamDesc[i];
                }
            }
            
            // @return
            if (cb.Return != null)
            {
                details += "\n\nReturn:\n    " + cb.Return;
            }
            return "\n\n" + details.Trim();
        }
        #endregion
    }

}
