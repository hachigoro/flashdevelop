using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ASCompletion.Completion;
using ASCompletion.Context;
using ASCompletion.Model;
using PluginCore;
using PluginCore.FRService;
using PluginCore.Helpers;
using PluginCore.Managers;
using PluginCore.Utilities;
using ProjectManager;
using ProjectManager.Projects;
using ScintillaNet;

namespace CodeRefactor.Provider
{
    /// <summary>
    /// Central repository of miscellaneous refactoring helper methods to be used by any refactoring command.
    /// </summary>
    public static class RefactoringHelper
    {
        /// <summary>
        /// Populates the m_SearchResults with any found matches
        /// </summary>
        public static IDictionary<string, List<SearchMatch>> GetInitialResultsList(FRResults results)
        {
            var searchResults = new Dictionary<string, List<SearchMatch>>();
            if (results is null)
            {
                // I suppose this should never happen -- 
                // normally invoked when the user cancels the FindInFiles dialogue.  
                // but since we're programmatically invoking the FRSearch, I don't think this should happen.
                // TODO: report this?
            }
            else if (results.Count == 0)
            {
                // no search results found.  Again, an interesting issue if this comes up.  
                // we should at least find the source entry the user is trying to change.
                // TODO: report this?
            }
            else
            {
                // found some matches!
                // I current separate the search listing from the FRResults.  It's probably unnecessary but this is just the initial implementation.
                // TODO: test if this is necessary
                foreach (var entry in results)
                {
                    searchResults.Add(entry.Key, new List<SearchMatch>());
                    foreach (var match in entry.Value)
                    {
                        searchResults[entry.Key].Add(match);
                    }
                }
            }
            return searchResults;
        }

        /// <summary>
        /// Gets if the language is valid for refactoring
        /// </summary>
        public static bool GetLanguageIsValid()
        {
            var document = PluginBase.MainForm.CurrentDocument;
            if (document is null || !document.IsEditable) return false;
            var lang = document.SciControl.ConfigurationLanguage;
            return CommandFactoryProvider.ContainsLanguage(lang);
        }

        /// <summary>
        /// Checks if the model is not null and file exists
        /// </summary>
        public static bool ModelFileExists(FileModel model)
        {
            return model != null && File.Exists(model.FileName);
        }

        /// <summary>
        /// Checks if the file is under the current SDK
        /// </summary>
        public static bool IsUnderSDKPath(FileModel model) => IsUnderSDKPath(model.FileName);

        public static bool IsUnderSDKPath(string file)
        {
            var path = PluginBase.CurrentSDK?.Path;
            return !string.IsNullOrEmpty(path) && file.StartsWithOrdinal(path);
        }

        /// <summary>
        /// Retrieves the refactoring target based on the current location.
        /// Note that this will look up to the declaration source.  
        /// This allows users to execute the rename from a reference to the source rather than having to navigate to the source first.
        /// </summary>
        public static ASResult GetDefaultRefactorTarget()
        {
            var sci = PluginBase.MainForm.CurrentDocument.SciControl;
            if (!ASContext.Context.IsFileValid || sci is null) return null;
            var position = sci.WordEndPosition(sci.CurrentPos, true);
            return ASComplete.GetExpressionType(sci, position);
        }

        public static MemberModel GetRefactoringTarget(ASResult target)
        {
            var type = target.Type;
            var member = target.Member;
            if (type.Flags.HasFlag(FlagType.Enum) && member is null || !type.IsVoid() && (member is null || (member.Flags & FlagType.Constructor) > 0))
                return type;
            return member;
        }

        public static string GetRefactorTargetName(ASResult target) => GetRefactoringTarget(target).Name;

        /// <summary>
        /// Retrieves the refactoring target based on the file.
        /// </summary>
        public static ASResult GetRefactorTargetFromFile(string path, DocumentHelper associatedDocumentHelper)
        {
            var sci = associatedDocumentHelper.LoadDocument(path)?.SciControl;
            if (sci is null) return null; // Should not happen...
            var fileName = Path.GetFileNameWithoutExtension(path);
            var line = 0;
            var classes = ASContext.Context.CurrentModel.Classes;
            if (classes.Count > 0)
            {
                foreach (var classModel in classes)
                {
                    if (classModel.Name.Equals(fileName))
                    {
                        // Optimization, we don't need to make a full lookup in this case
                        return new ASResult
                        {
                            IsStatic = true,
                            Type = classModel
                        };
                    }
                }
            }
            else
            {
                foreach (MemberModel member in ASContext.Context.CurrentModel.Members)
                {
                    if (member.Name.Equals(fileName))
                    {
                        line = member.LineFrom;
                        break;
                    }
                }
            }
            if (line > 0)
            {
                sci.SelectText(fileName, sci.PositionFromLine(line));
                return GetDefaultRefactorTarget();
            }

            return null;
        }

        /// <summary>
        /// Checks if a given search match actually points to the given target source
        /// </summary>
        /// <returns>True if the SearchMatch does point to the target source.</returns>
        public static ASResult DeclarationLookupResult(ScintillaControl sci, int position, DocumentHelper associatedDocumentHelper)
        {
            if (!ASContext.Context.IsFileValid || sci is null) return null;
            // get type at cursor position
            var result = ASComplete.GetExpressionType(sci, position);
            if (result.IsPackage) return result;
            // open source and show declaration
            if (result.IsNull()) return null;
            if (result.Member != null && (result.Member.Flags & FlagType.AutomaticVar) > 0) return null;
            var model = result.InFile ?? result.Member?.InFile ?? result.Type?.InFile;
            if (model is null || model.FileName == "") return null;
            var inClass = result.InClass ?? result.Type;
            // for Back command
            int lookupLine = sci.CurrentLine;
            int lookupCol = sci.CurrentPos - sci.PositionFromLine(lookupLine);
            ASContext.Panel.SetLastLookupPosition(ASContext.Context.CurrentFile, lookupLine, lookupCol);
            // open the file
            if (model != ASContext.Context.CurrentModel)
            {
                if (model.FileName.Length > 0 && File.Exists(model.FileName))
                {
                    if (!associatedDocumentHelper.FilesOpenedDocumentReferences.ContainsKey(model.FileName)) associatedDocumentHelper.LoadDocument(model.FileName);
                    sci = associatedDocumentHelper.FilesOpenedDocumentReferences[model.FileName].SciControl;
                }
                else
                {
                    ASComplete.OpenVirtualFile(model);
                    result.InFile = ASContext.Context.CurrentModel;
                    if (result.InFile is null) return null;
                    if (inClass != null)
                    {
                        inClass = result.InFile.GetClassByName(inClass.Name);
                        if (result.Member != null) result.Member = inClass.Members.Search(result.Member.Name, 0, 0);
                    }
                    else if (result.Member != null)
                    {
                        result.Member = result.InFile.Members.Search(result.Member.Name, 0, 0);
                    }
                    sci = ASContext.CurSciControl;
                }
            }
            if (sci is null) return null;
            if ((inClass is null || inClass.IsVoid()) && result.Member is null) return null;
            var line = 0;
            string name = null;
            var isClass = false;
            // member
            if (result.Member != null && result.Member.LineFrom > 0)
            {
                line = result.Member.LineFrom;
                name = result.Member.Name;
            }
            // class declaration
            else if (inClass != null && inClass.LineFrom > 0)
            {
                line = inClass.LineFrom;
                name = inClass.Name;
                isClass = true;
                // constructor
                foreach (MemberModel member in inClass.Members)
                {
                    if ((member.Flags & FlagType.Constructor) > 0)
                    {
                        line = member.LineFrom;
                        name = member.Name;
                        isClass = false;
                        break;
                    }
                }
            }
            if (line > 0) // select
            {
                if (isClass) ASComplete.LocateMember(sci, "(class|interface)", name, line);
                else ASComplete.LocateMember(sci, "(function|var|const|get|set|property|[,(])", name, line);
            }
            return result;
        }

        /// <summary>
        /// Simply checks the given flag combination if they contain a specific flag
        /// </summary>
        public static bool CheckFlag(FlagType flags, FlagType checkForThisFlag) => (flags & checkForThisFlag) == checkForThisFlag;

        /// <summary>
        /// Checks if the given match actually is the declaration.
        /// </summary>
        public static bool IsMatchTheTarget(ScintillaControl sci, SearchMatch match, ASResult target, DocumentHelper associatedDocumentHelper)
        {
            if (sci is null || target?.InFile is null || target.Member is null) return false;
            var originalFile = sci.FileName;
            // get type at match position
            var declaration = DeclarationLookupResult(sci, sci.MBSafePosition(match.Index) + sci.MBSafeTextLength(match.Value), associatedDocumentHelper);
            return declaration.InFile != null && originalFile == declaration.InFile.FileName && sci.CurrentPos == sci.MBSafePosition(match.Index) + sci.MBSafeTextLength(match.Value);
        }

        /// <summary>
        /// Checks if a given search match actually points to the given target source
        /// </summary>
        /// <returns>True if the SearchMatch does point to the target source.</returns>
        public static bool DoesMatchPointToTarget(ScintillaControl sci, SearchMatch match, ASResult target, DocumentHelper associatedDocumentHelper)
        {
            if (sci is null || target is null) return false;
            FileModel targetInFile = null;

            if (target.InFile != null)
                targetInFile = target.InFile;
            else if (target.Member != null && target.InClass is null)
                targetInFile = target.Member.InFile;

            var matchMember = targetInFile != null && target.Member != null;
            var matchType = target.Member is null && target.Type != null;
            if (!matchMember && !matchType) return false;

            ASResult result = null;
            // get type at match position
            if (match.Index < sci.Text.Length) // TODO: find out rare cases of incorrect index reported
            {
                result = DeclarationLookupResult(sci, sci.MBSafePosition(match.Index) + sci.MBSafeTextLength(match.Value), associatedDocumentHelper);
                // because the declaration lookup opens a document, we should register it with the document helper to be closed later
                associatedDocumentHelper?.RegisterLoadedDocument(PluginBase.MainForm.CurrentDocument);
            }
            // check if the result matches the target
            if (result is null || result.InFile is null && result.Type is null) return false;
            if (!matchMember) return result.Type != null && result.Type.QualifiedName == target.Type.QualifiedName;
            if (result.Member is null) return false;
            var resultInFile = result.InClass != null ? result.InFile : result.Member.InFile;
            return resultInFile.BasePath == targetInFile.BasePath
                   && resultInFile.FileName == targetInFile.FileName
                   && result.Member.LineFrom == target.Member.LineFrom
                   && result.Member.Name == target.Member.Name;
        }

        /// <summary>
        /// Finds the given target in all project files.
        /// If the target is a local variable or function parameter, it will only search the associated file.
        /// Note: if running asynchronously, you must supply a listener to "findFinishedHandler" to retrieve the results.
        /// If running synchronously, do not set listeners and instead use the return value.
        /// </summary>
        /// <param name="target">the source member to find references to</param>
        /// <param name="progressReportHandler">event to fire as search results are compiled</param>
        /// <param name="findFinishedHandler">event to fire once searching is finished</param>
        /// <param name="asynchronous">executes in asynchronous mode</param>
        /// <returns>If "asynchronous" is false, will return the search results, otherwise returns null on bad input or if running in asynchronous mode.</returns>
        public static FRResults FindTargetInFiles(ASResult target, FRProgressReportHandler progressReportHandler, FRFinishedHandler findFinishedHandler, bool asynchronous)
        {
            return FindTargetInFiles(target, progressReportHandler, findFinishedHandler, asynchronous, false, false);
        }

        /// <summary>
        /// Finds the given target in all project files.
        /// If the target is a local variable or function parameter, it will only search the associated file.
        /// Note: if running asynchronously, you must supply a listener to "findFinishedHandler" to retrieve the results.
        /// If running synchronously, do not set listeners and instead use the return value.
        /// </summary>
        /// <param name="target">the source member to find references to</param>
        /// <param name="progressReportHandler">event to fire as search results are compiled</param>
        /// <param name="findFinishedHandler">event to fire once searching is finished</param>
        /// <param name="asynchronous">executes in asynchronous mode</param>
        /// <param name="onlySourceFiles">searches only on defined classpaths</param>
        /// <returns>If "asynchronous" is false, will return the search results, otherwise returns null on bad input or if running in asynchronous mode.</returns>
        public static FRResults FindTargetInFiles(ASResult target, FRProgressReportHandler progressReportHandler, FRFinishedHandler findFinishedHandler, bool asynchronous, bool onlySourceFiles, bool ignoreSdkFiles)
        {
            return FindTargetInFiles(target, progressReportHandler, findFinishedHandler, asynchronous, onlySourceFiles, ignoreSdkFiles, false, false);
        }

        /// <summary>
        /// Finds the given target in all project files.
        /// If the target is a local variable or function parameter, it will only search the associated file.
        /// Note: if running asynchronously, you must supply a listener to "findFinishedHandler" to retrieve the results.
        /// If running synchronously, do not set listeners and instead use the return value.
        /// </summary>
        /// <param name="target">the source member to find references to</param>
        /// <param name="progressReportHandler">event to fire as search results are compiled</param>
        /// <param name="findFinishedHandler">event to fire once searching is finished</param>
        /// <param name="asynchronous">executes in asynchronous mode</param>
        /// <param name="onlySourceFiles">searches only on defined classpaths</param>
        /// <returns>If "asynchronous" is false, will return the search results, otherwise returns null on bad input or if running in asynchronous mode.</returns>
        public static FRResults FindTargetInFiles(ASResult target, FRProgressReportHandler progressReportHandler, FRFinishedHandler findFinishedHandler, bool asynchronous, bool onlySourceFiles, bool ignoreSdkFiles, bool includeComments, bool includeStrings)
        {
            if (target is null) return null;
            var member = target.Member;
            var type = target.Type;
            if ((member is null || string.IsNullOrEmpty(member.Name)) && (type is null || (type.Flags & (FlagType.Class | FlagType.Enum)) == 0))
            {
                return null;
            }
            FRConfiguration config;
            var project = PluginBase.CurrentProject;
            var file = PluginBase.MainForm.CurrentDocument.FileName;
            // This is out of the project, just look for this file...
            if (IsPrivateTarget(target) || !IsProjectRelatedFile(project, file))
            {
                var mask = Path.GetFileName(file);
                if (mask.Contains("[model]"))
                {
                    findFinishedHandler?.Invoke(new FRResults());
                    return null;
                }
                var path = Path.GetDirectoryName(file);
                config = new FRConfiguration(path, mask, false, GetFRSearch(member != null ? member.Name : type.Name, includeComments, includeStrings));
            }
            else if (member != null && !CheckFlag(member.Flags, FlagType.Constructor))
            {
                config = new FRConfiguration(GetAllProjectRelatedFiles(project, onlySourceFiles, ignoreSdkFiles), GetFRSearch(member.Name, includeComments, includeStrings));
            }
            else
            {
                target.Member = null;
                config = new FRConfiguration(GetAllProjectRelatedFiles(project, onlySourceFiles, ignoreSdkFiles), GetFRSearch(type.Name, includeComments, includeStrings));
            }
            config.CacheDocuments = true;
            var runner = new FRRunner();
            if (progressReportHandler != null) runner.ProgressReport += progressReportHandler;
            if (findFinishedHandler != null) runner.Finished += findFinishedHandler;
            if (asynchronous) runner.SearchAsync(config);
            else return runner.SearchSync(config);
            return null;
        }

        /// <summary>
        /// Checks if files is related to the project
        /// TODO support SWCs -> refactor test as IProject method
        /// </summary>
        public static bool IsProjectRelatedFile(IProject project, string file)
        {
            if (project is null) return false;
            var context = ASContext.GetLanguageContext(project.Language);
            if (context is null) return false;
            foreach (var pathModel in context.Classpath)
            {
                var absolute = project.GetAbsolutePath(pathModel.Path);
                if (file.StartsWithOrdinal(absolute)) return true;
            }
            // If no source paths are defined, is it under the project?
            if (project.SourcePaths.Length != 0) return false;
            var projRoot = Path.GetDirectoryName(project.ProjectPath);
            return file.StartsWithOrdinal(projRoot);
        }

        /// <summary>
        /// Gets all files related to the project
        /// </summary>
        private static List<string> GetAllProjectRelatedFiles(IProject project, bool onlySourceFiles)
        {
            return GetAllProjectRelatedFiles(project, onlySourceFiles, false);
        }
        private static List<string> GetAllProjectRelatedFiles(IProject project, bool onlySourceFiles, bool ignoreSdkFiles)
        {
            var files = new List<string>();
            var filter = project.DefaultSearchFilter;
            if (string.IsNullOrEmpty(filter)) return files;
            var filters = project.DefaultSearchFilter.Split(new[] {';'}, StringSplitOptions.RemoveEmptyEntries);
            if (!onlySourceFiles)
            {
                var context = ASContext.GetLanguageContext(project.Language);
                if (context is null) return files;
                foreach (var pathModel in context.Classpath)
                {
                    var absolute = project.GetAbsolutePath(pathModel.Path);
                    if (!Directory.Exists(absolute) || ignoreSdkFiles && IsUnderSDKPath(absolute)) continue;
                    foreach (var filterMask in filters)
                    {
                        files.AddRange(Directory.GetFiles(absolute, filterMask, SearchOption.AllDirectories));
                    }
                }
            }
            else
            {
                var lookupPaths = project.SourcePaths.Concat(ProjectManager.PluginMain.Settings.GetGlobalClasspaths(project.Language));
                if (project is Project p && p.AdditionalPaths != null) lookupPaths = lookupPaths.Concat(p.AdditionalPaths);
                lookupPaths = lookupPaths.Select(project.GetAbsolutePath).Distinct();
                foreach (var path in lookupPaths)
                {
                    if (!Directory.Exists(path) || ignoreSdkFiles && IsUnderSDKPath(path)) continue;
                    foreach (var filterMask in filters)
                    {
                        files.AddRange(Directory.GetFiles(path, filterMask, SearchOption.AllDirectories));
                    }
                }
            }
            // If no source paths are defined, get files directly from project path
            if (project.SourcePaths.Length == 0)
            {
                var projRoot = Path.GetDirectoryName(project.ProjectPath);
                foreach (var filterMask in filters)
                {
                    files.AddRange(Directory.GetFiles(projRoot, filterMask, SearchOption.AllDirectories));
                }
            }
            return files;
        }

        /// <summary>
        /// Generates an FRSearch to find all instances of the given member name.
        /// Enables WholeWord and Match Case. No comment/string literal, escape characters, or regex searching.
        /// </summary>
        internal static FRSearch GetFRSearch(string memberName, bool includeComments, bool includeStrings)
        {
            var search = new FRSearch(memberName);
            search.IsRegex = false;
            search.IsEscaped = false;
            search.WholeWord = true;
            search.NoCase = false;
            search.Filter = SearchFilter.None;

            if (!includeComments) search.Filter |= SearchFilter.OutsideCodeComments;
            if (!includeStrings) search.Filter |= SearchFilter.OutsideStringLiterals;

            return search;
        }

        /// <summary>
        /// Replaces only the matches in the current sci control
        /// </summary>
        public static void ReplaceMatches(List<SearchMatch> matches, ScintillaControl sci, string replacement)
        {
            if (sci is null || matches.IsNullOrEmpty()) return;
            sci.BeginUndoAction();
            try
            {
                for (int i = 0, matchCount = matches.Count; i < matchCount; i++)
                {
                    var match = matches[i];
                    SelectMatch(sci, match);
                    FRSearch.PadIndexes(matches, i + 1, match.Value, replacement);
                    sci.EnsureVisible(sci.LineFromPosition(sci.MBSafePosition(match.Index)));
                    sci.ReplaceSel(replacement);
                }
            }
            finally
            {
                sci.EndUndoAction();
            }
        }

        /// <summary>
        /// Selects a search match
        /// </summary>
        public static void SelectMatch(ScintillaControl sci, SearchMatch match)
        {
            if (sci is null || match is null) return;
            var start = sci.MBSafePosition(match.Index); // wchar to byte position
            var end = start + sci.MBSafeTextLength(match.Value); // wchar to byte text length
            var line = sci.LineFromPosition(start);
            sci.EnsureVisible(line);
            sci.SetSel(start, end);
        }

        /// <summary>
        /// Copies found file or directory based on the specified paths.
        /// If affected file was designated as a Document Class, updates project accordingly.
        /// </summary>
        /// <param name="oldPath"></param>
        /// <param name="newPath"></param>
        public static void Copy(string oldPath, string newPath) => Copy(oldPath, newPath, true);

        public static void Copy(string oldPath, string newPath, bool renaming) => Copy(oldPath, newPath, renaming, true);

        public static void Copy(string oldPath, string newPath, bool renaming, bool simulateMove)
        {
            if (string.IsNullOrEmpty(oldPath) || string.IsNullOrEmpty(newPath)) return;
            var project = (Project)PluginBase.CurrentProject;
            string newDocumentClass = null;

            if (File.Exists(oldPath) && FileHelper.ConfirmOverwrite(newPath))
            {
                File.Copy(oldPath, newPath, true);
                if (simulateMove)
                {
                    DocumentManager.MoveDocuments(oldPath, newPath);
                    if (project.IsDocumentClass(oldPath)) newDocumentClass = newPath;
                }
            }
            else if (Directory.Exists(oldPath))
            {
                newPath = renaming ? Path.Combine(Path.GetDirectoryName(oldPath), newPath) : Path.Combine(newPath, Path.GetFileName(oldPath));
                if (!FileHelper.ConfirmOverwrite(newPath)) return;
                if (simulateMove)
                {
                    // We need to use our own method for moving directories if folders in the new path already exist
                    FileHelper.CopyDirectory(oldPath, newPath, true);
                    foreach (var pattern in project.DefaultSearchFilter.Split(new[] {';'}, StringSplitOptions.RemoveEmptyEntries))
                    {
                        foreach (var file in Directory.GetFiles(oldPath, pattern, SearchOption.AllDirectories))
                        {
                            if (project.IsDocumentClass(file))
                            {
                                newDocumentClass = file.Replace(oldPath, newPath);
                                break;
                            }
                            DocumentManager.MoveDocuments(oldPath, newPath);
                        }
                        if (newDocumentClass != null) break;
                    }
                }
            }
            if (!string.IsNullOrEmpty(newDocumentClass))
            {
                project.SetDocumentClass(newDocumentClass, true);
                project.Save();
            }
        }

        /// <summary>
        /// Moves found file or directory based on the specified paths.
        /// If affected file was designated as a Document Class, updates project accordingly.
        /// </summary>
        /// <param name="oldPath"></param>
        /// <param name="newPath"></param>
        public static void Move(string oldPath, string newPath) => Move(oldPath, newPath, true);

        public static void Move(string oldPath, string newPath, bool renaming) => Move(oldPath, newPath, renaming, oldPath);

        public static void Move(string oldPath, string newPath, bool renaming, string originalOld)
        {
            if (string.IsNullOrEmpty(oldPath) || string.IsNullOrEmpty(newPath)) return;
            var project = (Project)PluginBase.CurrentProject;
            string newDocumentClass = null;

            if (File.Exists(oldPath) && FileHelper.ConfirmOverwrite(newPath))
            {
                FileHelper.ForceMove(oldPath, newPath);
                DocumentManager.MoveDocuments(oldPath, newPath);
                RaiseMoveEvent(originalOld, newPath);

                if (project.IsDocumentClass(oldPath)) newDocumentClass = newPath;
            }
            else if (Directory.Exists(oldPath))
            {
                newPath = renaming ? Path.Combine(Path.GetDirectoryName(oldPath), newPath) : Path.Combine(newPath, Path.GetFileName(oldPath));
                if (!FileHelper.ConfirmOverwrite(newPath)) return;
                var searchPattern = project.DefaultSearchFilter;
                foreach (var pattern in searchPattern.Split(new[] {';'}, StringSplitOptions.RemoveEmptyEntries))
                {
                    foreach (var file in Directory.GetFiles(oldPath, pattern, SearchOption.AllDirectories))
                    {
                        if (project.IsDocumentClass(file))
                        {
                            newDocumentClass = file.Replace(oldPath, newPath);
                            break;
                        }
                    }
                    if (newDocumentClass != null) break;
                }
                // We need to use our own method for moving directories if folders in the new path already exist
                FileHelper.ForceMoveDirectory(oldPath, newPath);
                DocumentManager.MoveDocuments(oldPath, newPath);
                RaiseMoveEvent(originalOld, newPath);
            }
            if (!string.IsNullOrEmpty(newDocumentClass))
            {
                project.SetDocumentClass(newDocumentClass, true);
                project.Save();
            }
        }
        
        public static bool IsInsideCommentOrString(SearchMatch match, ScintillaControl sci, bool includeComments, bool includeStrings)
        {
            var style = sci.BaseStyleAt(match.Index);
            return includeComments && IsCommentStyle(style) || includeStrings && IsStringStyle(style);
        }

        public static bool IsCommentStyle(int style)
        {
            return style switch
            {
                1 => true, //COMMENT
                2 => true, //COMMENTLINE
                3 => true, //COMMENTDOC
                15 => true,//COMMENTLINEDOC
                _ => false,
            };
        }

        public static bool IsStringStyle(int style)
        {
            return style switch
            {
                6 => true, //STRING
                7 => true, //CHARACTER
                13 => true,//VERBATIM
                14 => true,//REGEX
                _ => false,
            };
        }

        internal static void RaiseMoveEvent(string fromPath, string toPath)
        {
            if (Directory.Exists(toPath))
            {
                foreach (var file in Directory.EnumerateFiles(toPath))
                    RaiseMoveEvent(Path.Combine(fromPath, file), Path.Combine(toPath, file));
                foreach (var folder in Directory.EnumerateDirectories(toPath))
                    RaiseMoveEvent(Path.Combine(fromPath, folder), Path.Combine(toPath, folder));
            }
            else if (File.Exists(toPath))
            {
                var data = new Hashtable
                {
                    ["fromPath"] = fromPath,
                    ["toPath"] = toPath
                };
                var de = new DataEvent(EventType.Command, ProjectManagerEvents.FileMoved, data);
                EventManager.DispatchEvent(null, de);
            }
        }

        internal static bool IsPrivateTarget(ASResult target)
        {
            if (target.IsPackage) return false;
            var member = target.Member;
            if (member != null)
            {
                return member.Access == Visibility.Private && !target.InFile.haXe || (member.Flags & FlagType.LocalVar) > 0 || (member.Flags & FlagType.ParameterVar) > 0;
            }
            var type = target.Type;
            return type != null && type.Access == Visibility.Private && (!type.InFile.haXe || new SemVer(PluginBase.CurrentSDK.Version) < "4.0.0");
        }
    }

}
