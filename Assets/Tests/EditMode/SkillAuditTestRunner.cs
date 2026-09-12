using System.IO;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;
namespace StrategyCore.Tests
{
    [InitializeOnLoad] public static class SkillAuditTestRunner
    {
        const string Root = @"C:\Users\aleks\Documents\Codex\2026-09-11\v\work\skills-28\";
        static TestRunnerApi api;
        static SkillAuditTestRunner() { EditorApplication.update += Poll; }
        static void Poll()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists(Root+"run-edit-tests.txt")) return;
            File.Delete(Root+"run-edit-tests.txt");
            api=ScriptableObject.CreateInstance<TestRunnerApi>(); api.RegisterCallbacks(new Results());
            api.Execute(new ExecutionSettings(new Filter { testMode=TestMode.EditMode, assemblyNames=new[]{"Interflow.Tests.EditMode"} }));
        }
        class Results : ICallbacks
        {
            public void RunStarted(ITestAdaptor testsToRun) { File.WriteAllText(Root+"tests-started.txt",testsToRun.Name); }
            public void RunFinished(ITestResultAdaptor result) { TestRunnerApi.SaveResultToFile(result,Root+"edit-results.xml"); }
            public void TestStarted(ITestAdaptor test) {}
            public void TestFinished(ITestResultAdaptor result) {}
        }
    }
}
