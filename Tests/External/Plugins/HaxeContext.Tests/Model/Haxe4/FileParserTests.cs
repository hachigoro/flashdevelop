﻿using System.Collections.Generic;
using ASCompletion;
using ASCompletion.Context;
using NUnit.Framework;
using PluginCore;

namespace HaXeContext.Model.Haxe4
{
    [TestFixture]
    class FileParserTests : ASCompletionTests
    {
        [TestFixtureSetUp]
        public void Setup()
        {
            SetHaxeFeatures(sci);
            ASContext.Context.Settings.InstalledSDKs = new[] {new InstalledSDK {Path = PluginBase.CurrentProject.CurrentSDK, Version = "4.0.0"}};
        }

        static IEnumerable<TestCaseData> Issue2801TestCases
        {
            get
            {
                yield return new TestCaseData("Issue2801_1")
                    .Returns("\"A\"")
                    .SetName("Haxe4. Abstract default value. Issue 2801. Case 1")
                    .SetDescription("https://github.com/fdorg/flashdevelop/issues/2801");
                yield return new TestCaseData("Issue2801_2")
                    .Returns("\"A\"")
                    .SetName("Haxe4. Abstract default value. Issue 2801. Case 2")
                    .SetDescription("https://github.com/fdorg/flashdevelop/issues/2801");
                yield return new TestCaseData("Issue2801_3")
                    .Returns("1")
                    .SetName("Haxe4. Abstract default value. Issue 2801. Case 3")
                    .SetDescription("https://github.com/fdorg/flashdevelop/issues/2801");
                yield return new TestCaseData("Issue2801_4")
                    .Returns("6")
                    .SetName("Haxe4. Abstract default value. Issue 2801. Case 4")
                    .SetDescription("https://github.com/fdorg/flashdevelop/issues/2801");
            }
        }

        static IEnumerable<TestCaseData> Issue2836TestCases
        {
            get
            {
                yield return new TestCaseData("Issue2836_1")
                    .Returns("6")
                    .SetName("Haxe4. Abstract default value. Issue 2836. Case 1")
                    .SetDescription("https://github.com/fdorg/flashdevelop/issues/2836");
                yield return new TestCaseData("Issue2836_2")
                    .Returns("0")
                    .SetName("Haxe4. Abstract default value. Issue 2836. Case 2")
                    .SetDescription("https://github.com/fdorg/flashdevelop/issues/2836");
                yield return new TestCaseData("Issue2836_3")
                    .Returns("1")
                    .SetName("Haxe4. Abstract default value. Issue 2836. Case 3")
                    .SetDescription("https://github.com/fdorg/flashdevelop/issues/2836");
                yield return new TestCaseData("Issue2836_4")
                    .Returns("2")
                    .SetName("Haxe4. Abstract default value. Issue 2836. Case 4")
                    .SetDescription("https://github.com/fdorg/flashdevelop/issues/2836");
                yield return new TestCaseData("Issue2836_5")
                    .Returns("2")
                    .SetName("Haxe4. Abstract default value. Issue 2836. Case 5")
                    .SetDescription("https://github.com/fdorg/flashdevelop/issues/2836");
            }
        }

        [
            Test,
            TestCaseSource(nameof(Issue2801TestCases)),
            TestCaseSource(nameof(Issue2836TestCases)),
        ]
        public string ParseFile_Issue2801(string fileName)
        {
            var sourceText = Haxe3.FileParserTests.ReadAllText(fileName);
            SetSrc(sci, sourceText);
            var member = ASContext.Context.CurrentModel
                .Classes.FirstOrDefault(sci.CurrentLine)
                .Members.FirstOrDefault(sci.CurrentLine);
            return member.Value;
        }
    }
}