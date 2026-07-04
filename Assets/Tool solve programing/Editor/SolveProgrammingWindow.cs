using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

public sealed class SolveProgrammingWindow : EditorWindow
{
    private const string ExerciseFolder = "Assets/Tool solve programing/Exercises";
    private const string DefaultStudentFile = "Assets/Tool solve programing/StudentCode/BaiLamHocSinh.cs";
    private const string LastExerciseKey = "SolveProgramming.LastExerciseId";
    private const string LastStudentScriptKey = "SolveProgramming.LastStudentScript";
    private readonly List<ExerciseDefinition> exercises = new List<ExerciseDefinition>();
    private readonly List<TestResult> testResults = new List<TestResult>();

    private Vector2 scroll;
    private int selectedExerciseIndex;
    private MonoScript selectedStudentScript;
    private string statusMessage = "Chọn bài tập và bấm Test.";
    private ResultState resultState = ResultState.None;

    [MenuItem("Tools/Solve Programming/Test Hoc Sinh")]
    public static void Open()
    {
        GetWindow<SolveProgrammingWindow>("Solve Programming");
    }

    private void OnEnable()
    {
        LoadExercises();
        RestoreSelection();
        RestoreStudentScript();
    }

    private void OnGUI()
    {
        DrawToolbar();

        if (EditorApplication.isCompiling)
        {
            EditorGUILayout.HelpBox("Unity đang compile. Vui lòng đợi xong rồi bấm Test.", MessageType.Info);
        }

        if (exercises.Count == 0)
        {
            EditorGUILayout.HelpBox("Chưa có bài tập JSON trong Assets/Tool solve programing/Exercises.", MessageType.Warning);
            if (GUILayout.Button("Reload bài tập"))
            {
                LoadExercises();
            }
            return;
        }

        DrawExercisePicker();
        DrawExerciseDetails();
        DrawActionButtons();
        DrawStatus();
        DrawTestResults();
    }

    private void DrawToolbar()
    {
        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            GUILayout.Label("Solve Programming", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();

            if (GUILayout.Button("Reload", EditorStyles.toolbarButton, GUILayout.Width(70)))
            {
                LoadExercises();
                RestoreSelection();
            }
        }
    }

    private void DrawExercisePicker()
    {
        var titles = exercises.Select(exercise => exercise.title).ToArray();
        var newIndex = EditorGUILayout.Popup("Bài tập", selectedExerciseIndex, titles);
        if (newIndex != selectedExerciseIndex)
        {
            selectedExerciseIndex = newIndex;
            EditorPrefs.SetString(LastExerciseKey, exercises[selectedExerciseIndex].id);
            testResults.Clear();
            RestoreStudentScript();
            SetStatus(ResultState.None, "Đã chọn bài tập. Bấm Test để kiểm tra.");
        }
    }

    private void DrawExerciseDetails()
    {
        var exercise = exercises[selectedExerciseIndex];

        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField(exercise.title, EditorStyles.boldLabel);
        EditorGUILayout.LabelField(exercise.description, EditorStyles.boldLabel);

        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            if (IsMainExercise(exercise))
            {
                EditorGUILayout.LabelField("Kiểu test", "Main + List<object> input");
                EditorGUILayout.LabelField("Hàm cần có", "public static object Main(List<object> input)");
                EditorGUILayout.LabelField("Kiểu dữ liệu input", exercise.inputTypes == null ? "" : string.Join(", ", exercise.inputTypes));
                EditorGUILayout.LabelField("Kiểu dữ liệu kết quả", exercise.expectedType);
            }
            else
            {
                EditorGUILayout.LabelField("Class", exercise.studentClass);
                EditorGUILayout.LabelField("Hàm cần làm", BuildMethodSignature(exercise));
            }

            EditorGUILayout.LabelField("File học sinh", string.IsNullOrWhiteSpace(exercise.studentFile) ? DefaultStudentFile : exercise.studentFile);
        }

        if (IsMainExercise(exercise))
        {
            DrawStudentScriptDropArea();
        }
    }

    private void DrawActionButtons()
    {
        var exercise = exercises[selectedExerciseIndex];

        using (new EditorGUILayout.HorizontalScope())
        {
            GUI.enabled = !EditorApplication.isCompiling;

            var oldColor = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.55f, 0.8f, 1f);
            if (GUILayout.Button("Test", GUILayout.Height(34)))
            {
                StartCheck();
            }
            GUI.backgroundColor = oldColor;

            GUI.enabled = true;

            if (GUILayout.Button("Tạo code mẫu", GUILayout.Height(34), GUILayout.Width(130)))
            {
                CreateStudentTemplate(exercise);
            }

            if (GUILayout.Button("Mở file học sinh", GUILayout.Height(34), GUILayout.Width(140)))
            {
                OpenStudentFile(exercise);
            }
        }
    }

    private void CreateStudentTemplate(ExerciseDefinition exercise)
    {
        var assetPath = string.IsNullOrWhiteSpace(exercise.studentFile) ? DefaultStudentFile : exercise.studentFile;
        if (!assetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
        {
            SetStatus(ResultState.Fail, "Đường dẫn file học sinh phải nằm trong thư mục Assets.");
            return;
        }

        if (File.Exists(assetPath))
        {
            var overwrite = EditorUtility.DisplayDialog(
                "Tạo code mẫu",
                "File học sinh đã tồn tại:\n" + assetPath + "\n\nBạn có muốn ghi đè bằng code mẫu mới không?",
                "Ghi đè",
                "Hủy");

            if (!overwrite)
            {
                return;
            }
        }

        var directory = Path.GetDirectoryName(assetPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(assetPath, BuildStudentTemplate(exercise));
        AssetDatabase.ImportAsset(assetPath);
        AssetDatabase.Refresh();

        selectedStudentScript = AssetDatabase.LoadAssetAtPath<MonoScript>(assetPath);
        SaveSelectedStudentScript();
        testResults.Clear();
        SetStatus(ResultState.None, "Đã tạo code mẫu. Học sinh chỉ cần viết code bên trong hàm Main rồi bấm Test.");
        OpenStudentFile(exercise);
    }

    private static string BuildStudentTemplate(ExerciseDefinition exercise)
    {
        var inputTypes = exercise.inputTypes ?? Array.Empty<string>();
        var lines = new List<string>
        {
            "using System.Collections.Generic;",
            "",
            "public static class Program",
            "{",
            "    public static object Main(List<object> input)",
            "    {",
            "        // Chỉ viết code trong hàm này. Không cần tạo class mới."
        };

        for (var i = 0; i < inputTypes.Length; i++)
        {
            lines.Add("        var giaTri" + (i + 1) + " = (" + GetCSharpTypeName(inputTypes[i]) + ")input[" + i + "];");
        }

        lines.Add("");
        lines.Add("        // TODO: thay dòng return bên dưới bằng kết quả của bài toán.");
        lines.Add("        return " + GetDefaultReturnValue(exercise.expectedType) + ";");
        lines.Add("    }");
        lines.Add("}");

        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }

    private static string GetCSharpTypeName(string typeName)
    {
        switch ((typeName ?? "").Trim().ToLowerInvariant())
        {
            case "int":
            case "system.int32":
                return "int";
            case "float":
            case "single":
            case "system.single":
                return "float";
            case "double":
            case "system.double":
                return "double";
            case "bool":
            case "boolean":
            case "system.boolean":
                return "bool";
            case "string":
            case "system.string":
                return "string";
            default:
                return "object";
        }
    }

    private static string GetDefaultReturnValue(string typeName)
    {
        switch ((typeName ?? "").Trim().ToLowerInvariant())
        {
            case "int":
            case "system.int32":
                return "0";
            case "float":
            case "single":
            case "system.single":
                return "0f";
            case "double":
            case "system.double":
                return "0d";
            case "bool":
            case "boolean":
            case "system.boolean":
                return "false";
            case "string":
            case "system.string":
                return "\"\"";
            default:
                return "null";
        }
    }

    private void DrawStudentScriptDropArea()
    {
        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("File code học sinh", EditorStyles.boldLabel);

        EditorGUI.BeginChangeCheck();
        selectedStudentScript = (MonoScript)EditorGUILayout.ObjectField("Kéo thả file .cs", selectedStudentScript, typeof(MonoScript), false);
        if (EditorGUI.EndChangeCheck())
        {
            SaveSelectedStudentScript();
        }

        var dropRect = GUILayoutUtility.GetRect(0f, 54f, GUILayout.ExpandWidth(true));
        GUI.Box(dropRect, selectedStudentScript == null ? "Kéo thả file .cs vào đây hoặc bấm Tạo code mẫu" : AssetDatabase.GetAssetPath(selectedStudentScript), EditorStyles.helpBox);

        var currentEvent = Event.current;
        if (!dropRect.Contains(currentEvent.mousePosition))
        {
            return;
        }

        if (currentEvent.type == EventType.DragUpdated || currentEvent.type == EventType.DragPerform)
        {
            var script = DragAndDrop.objectReferences.OfType<MonoScript>().FirstOrDefault() ?? LoadDraggedScriptFromPath();
            DragAndDrop.visualMode = script == null ? DragAndDropVisualMode.Rejected : DragAndDropVisualMode.Copy;

            if (currentEvent.type == EventType.DragPerform && script != null)
            {
                DragAndDrop.AcceptDrag();
                selectedStudentScript = script;
                SaveSelectedStudentScript();
                testResults.Clear();
                SetStatus(ResultState.None, "Đã nhận file code. Bấm Test để kiểm tra.");
            }

            currentEvent.Use();
        }
    }

    private static MonoScript LoadDraggedScriptFromPath()
    {
        foreach (var path in DragAndDrop.paths)
        {
            if (!path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var normalizedPath = path.Replace('\\', '/');
            var projectPath = Directory.GetCurrentDirectory().Replace('\\', '/');
            if (normalizedPath.StartsWith(projectPath + "/", StringComparison.OrdinalIgnoreCase))
            {
                normalizedPath = normalizedPath.Substring(projectPath.Length + 1);
            }

            if (normalizedPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            {
                return AssetDatabase.LoadAssetAtPath<MonoScript>(normalizedPath);
            }
        }

        return null;
    }

    private void DrawStatus()
    {
        EditorGUILayout.Space(8);

        DrawColoredMessageBox(statusMessage, resultState, true);
    }

    private void DrawTestResults()
    {
        if (testResults.Count == 0)
        {
            return;
        }

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Kết quả từng test", EditorStyles.boldLabel);

        scroll = EditorGUILayout.BeginScrollView(scroll);
        foreach (var result in testResults)
        {
            DrawColoredMessageBox((result.passed ? "PASS: " : "FAIL: ") + result.name + "\n" + result.message, result.passed ? ResultState.Pass : ResultState.Fail, false);
        }
        EditorGUILayout.EndScrollView();
    }

    private static void DrawColoredMessageBox(string message, ResultState state, bool large)
    {
        var backgroundColor = GetStateColor(state);
        var borderColor = GetStateBorderColor(state);
        var textColor = GetStateTextColor(state);
        var style = new GUIStyle(EditorStyles.wordWrappedLabel)
        {
            fontStyle = large ? FontStyle.Bold : FontStyle.Normal,
            normal = { textColor = textColor },
            padding = new RectOffset(10, 10, 8, 8)
        };

        var height = Mathf.Max(large ? 42f : 54f, style.CalcHeight(new GUIContent(message), EditorGUIUtility.currentViewWidth - 36f));
        var rect = GUILayoutUtility.GetRect(0f, height, GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(rect, backgroundColor);

        var topBorder = new Rect(rect.x, rect.y, rect.width, 1f);
        var bottomBorder = new Rect(rect.x, rect.yMax - 1f, rect.width, 1f);
        var leftBorder = new Rect(rect.x, rect.y, 1f, rect.height);
        var rightBorder = new Rect(rect.xMax - 1f, rect.y, 1f, rect.height);
        EditorGUI.DrawRect(topBorder, borderColor);
        EditorGUI.DrawRect(bottomBorder, borderColor);
        EditorGUI.DrawRect(leftBorder, borderColor);
        EditorGUI.DrawRect(rightBorder, borderColor);

        GUI.Label(rect, message, style);
    }

    private void StartCheck()
    {
        testResults.Clear();

        if (EditorApplication.isCompiling)
        {
            SetStatus(ResultState.Waiting, "Unity đang compile. Đợi Unity compile xong rồi bấm Test lại.");
            return;
        }

        SetStatus(ResultState.Waiting, "Đang chạy test...");
        RunSelectedExercise();
    }

    private void RunSelectedExercise()
    {
        if (selectedExerciseIndex < 0 || selectedExerciseIndex >= exercises.Count)
        {
            SetStatus(ResultState.Fail, "Chưa chọn bài tập hợp lệ.");
            return;
        }

        testResults.Clear();
        var exercise = exercises[selectedExerciseIndex];
        var validationError = ValidateExercise(exercise);
        if (!string.IsNullOrEmpty(validationError))
        {
            SetStatus(ResultState.Fail, validationError);
            return;
        }

        try
        {
            if (IsMainExercise(exercise))
            {
                RunMainExercise(exercise);
                return;
            }

            var type = FindType(exercise.studentClass);
            if (type == null)
            {
                SetStatus(ResultState.Fail, "Không tìm thấy class " + exercise.studentClass + ". Kiểm tra tên class trong file học sinh.");
                return;
            }

            var parameterTypes = exercise.parameterTypes.Select(ResolveSupportedType).ToArray();
            var method = type.GetMethod(exercise.method, BindingFlags.Public | BindingFlags.Static, null, parameterTypes, null);
            if (method == null)
            {
                SetStatus(ResultState.Fail, "Không tìm thấy hàm public static " + BuildMethodSignature(exercise) + ".");
                return;
            }

            var expectedReturnType = ResolveSupportedType(exercise.returnType);
            if (method.ReturnType != expectedReturnType)
            {
                SetStatus(ResultState.Fail, "Kiểu trả về của hàm phải là " + exercise.returnType + ".");
                return;
            }

            foreach (var testCase in exercise.testCases)
            {
                testResults.Add(RunTestCase(method, parameterTypes, expectedReturnType, testCase));
            }

            var passedCount = testResults.Count(result => result.passed);
            if (passedCount == testResults.Count)
            {
                SetStatus(ResultState.Pass, "PASS: Tất cả " + passedCount + "/" + testResults.Count + " test đều đúng.");
            }
            else
            {
                SetStatus(ResultState.Fail, "FAIL: Đúng " + passedCount + "/" + testResults.Count + " test. Xem chi tiết bên dưới.");
            }
        }
        catch (Exception exception)
        {
            SetStatus(ResultState.Fail, "Tool gặp lỗi khi test: " + exception.Message);
        }
    }

    private void RunMainExercise(ExerciseDefinition exercise)
    {
        var script = GetSelectedStudentScript(exercise);
        if (script == null)
        {
            SetStatus(ResultState.Fail, "Hãy kéo thả file .cs của học sinh vào tool trước khi bấm Test, hoặc bấm Tạo code mẫu.");
            return;
        }

        var scriptPath = AssetDatabase.GetAssetPath(script);
        if (string.IsNullOrEmpty(scriptPath))
        {
            SetStatus(ResultState.Fail, "File code chưa nằm trong Assets của Unity. Hãy copy file vào project rồi kéo thả lại.");
            return;
        }

        var mainMethod = FindMainMethod(scriptPath, exercise.mainClass);
        if (mainMethod == null)
        {
            SetStatus(ResultState.Fail, "Không tìm thấy hàm public static object Main(List<object> input) trong file: " + scriptPath);
            return;
        }

        foreach (var testCase in exercise.testCases)
        {
            testResults.Add(RunMainTestCase(mainMethod, exercise, testCase));
        }

        var passedCount = testResults.Count(result => result.passed);
        if (passedCount == testResults.Count)
        {
            SetStatus(ResultState.Pass, "PASS: Tất cả " + passedCount + "/" + testResults.Count + " test đều đúng.");
        }
        else
        {
            SetStatus(ResultState.Fail, "FAIL: Đúng " + passedCount + "/" + testResults.Count + " test. Xem chi tiết bên dưới.");
        }
    }

    private TestResult RunMainTestCase(MethodInfo mainMethod, ExerciseDefinition exercise, ExerciseTestCase testCase)
    {
        try
        {
            if (testCase.inputValues == null || testCase.inputValues.Length != exercise.inputTypes.Length)
            {
                return TestResult.Fail(testCase.name, "Số lượng inputValues không khớp với inputTypes.");
            }

            var input = new List<object>();
            for (var i = 0; i < exercise.inputTypes.Length; i++)
            {
                input.Add(ConvertString(testCase.inputValues[i], ResolveSupportedType(exercise.inputTypes[i])));
            }

            var expectedType = ResolveSupportedType(exercise.expectedType);
            var expected = ConvertString(testCase.expectedValue ?? testCase.expected, expectedType);
            var actual = mainMethod.Invoke(null, new object[] { input });
            var resultMessage = "Input: " + FormatObjectList(input) + "\nMong đợi: " + FormatValue(expected) + "\nNhận được: " + FormatValue(actual);

            if (ValuesEqual(expected, actual, expectedType))
            {
                return TestResult.Pass(testCase.name, resultMessage);
            }

            return TestResult.Fail(testCase.name, resultMessage);
        }
        catch (TargetInvocationException exception)
        {
            var innerMessage = exception.InnerException != null ? exception.InnerException.Message : exception.Message;
            return TestResult.Fail(testCase.name, "Main bị lỗi khi chạy: " + innerMessage);
        }
        catch (Exception exception)
        {
            return TestResult.Fail(testCase.name, "Không chạy được test: " + exception.Message);
        }
    }

    private TestResult RunTestCase(MethodInfo method, Type[] parameterTypes, Type expectedReturnType, ExerciseTestCase testCase)
    {
        try
        {
            if (testCase.args == null || testCase.args.Length != parameterTypes.Length)
            {
                return TestResult.Fail(testCase.name, "Số lượng tham số trong test case không khớp với hàm.");
            }

            var args = new object[parameterTypes.Length];
            for (var i = 0; i < parameterTypes.Length; i++)
            {
                args[i] = ConvertString(testCase.args[i], parameterTypes[i]);
            }

            var expected = ConvertString(testCase.expected, expectedReturnType);
            var actual = method.Invoke(null, args);

            if (ValuesEqual(expected, actual, expectedReturnType))
            {
                return TestResult.Pass(testCase.name, "Kết quả: " + FormatValue(actual));
            }

            return TestResult.Fail(testCase.name, "Mong đợi: " + FormatValue(expected) + " | Nhận được: " + FormatValue(actual));
        }
        catch (TargetInvocationException exception)
        {
            var innerMessage = exception.InnerException != null ? exception.InnerException.Message : exception.Message;
            return TestResult.Fail(testCase.name, "Hàm bị lỗi khi chạy: " + innerMessage);
        }
        catch (Exception exception)
        {
            return TestResult.Fail(testCase.name, "Không chạy được test: " + exception.Message);
        }
    }

    private void LoadExercises()
    {
        exercises.Clear();

        if (!Directory.Exists(ExerciseFolder))
        {
            Directory.CreateDirectory(ExerciseFolder);
        }

        foreach (var guid in AssetDatabase.FindAssets("t:TextAsset", new[] { ExerciseFolder }))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            if (!path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var json = File.ReadAllText(path);
            var file = JsonUtility.FromJson<ExerciseFile>(json);
            if (file == null || file.exercises == null)
            {
                continue;
            }

            foreach (var exercise in file.exercises)
            {
                if (exercise != null)
                {
                    exercises.Add(exercise);
                }
            }
        }

        if (selectedExerciseIndex >= exercises.Count)
        {
            selectedExerciseIndex = 0;
        }
    }

    private void RestoreSelection()
    {
        if (exercises.Count == 0)
        {
            selectedExerciseIndex = 0;
            return;
        }

        var lastId = EditorPrefs.GetString(LastExerciseKey, "");
        var index = exercises.FindIndex(exercise => exercise.id == lastId);
        selectedExerciseIndex = index >= 0 ? index : 0;
    }

    private static string ValidateExercise(ExerciseDefinition exercise)
    {
        if (IsMainExercise(exercise))
        {
            if (exercise.inputTypes == null)
            {
                return "Bài tập thiếu inputTypes.";
            }

            foreach (var inputType in exercise.inputTypes)
            {
                if (ResolveSupportedType(inputType) == null)
                {
                    return "inputType chưa được hỗ trợ: " + inputType;
                }
            }

            if (string.IsNullOrWhiteSpace(exercise.expectedType))
            {
                return "Bài tập thiếu expectedType.";
            }

            if (ResolveSupportedType(exercise.expectedType) == null)
            {
                return "expectedType chưa được hỗ trợ: " + exercise.expectedType;
            }

            if (exercise.testCases == null || exercise.testCases.Length == 0)
            {
                return "Bài tập chưa có testCases.";
            }

            foreach (var testCase in exercise.testCases)
            {
                if (testCase == null)
                {
                    return "Bài tập có test case rỗng.";
                }

                if (testCase.inputValues == null || testCase.inputValues.Length != exercise.inputTypes.Length)
                {
                    return "Test case " + testCase.name + " có inputValues không khớp inputTypes.";
                }

                if (testCase.expectedValue == null && testCase.expected == null)
                {
                    return "Test case thiếu expectedValue.";
                }
            }

            return "";
        }

        if (string.IsNullOrWhiteSpace(exercise.studentClass))
        {
            return "Bài tập thiếu studentClass.";
        }

        if (string.IsNullOrWhiteSpace(exercise.method))
        {
            return "Bài tập thiếu method.";
        }

        if (string.IsNullOrWhiteSpace(exercise.returnType))
        {
            return "Bài tập thiếu returnType.";
        }

        if (exercise.parameterTypes == null)
        {
            return "Bài tập thiếu parameterTypes.";
        }

        if (exercise.testCases == null || exercise.testCases.Length == 0)
        {
            return "Bài tập chưa có testCases.";
        }

        if (ResolveSupportedType(exercise.returnType) == null)
        {
            return "returnType chưa được hỗ trợ: " + exercise.returnType;
        }

        foreach (var parameterType in exercise.parameterTypes)
        {
            if (ResolveSupportedType(parameterType) == null)
            {
                return "parameterType chưa được hỗ trợ: " + parameterType;
            }
        }

        return "";
    }

    private static bool IsMainExercise(ExerciseDefinition exercise)
    {
        return string.Equals(exercise.testMode, "main", StringComparison.OrdinalIgnoreCase);
    }

    private MonoScript GetSelectedStudentScript(ExerciseDefinition exercise)
    {
        if (selectedStudentScript != null)
        {
            return selectedStudentScript;
        }

        var path = string.IsNullOrWhiteSpace(exercise.studentFile) ? DefaultStudentFile : exercise.studentFile;
        return AssetDatabase.LoadAssetAtPath<MonoScript>(path);
    }

    private void RestoreStudentScript()
    {
        var path = EditorPrefs.GetString(LastStudentScriptKey, "");
        if (string.IsNullOrWhiteSpace(path) && selectedExerciseIndex >= 0 && selectedExerciseIndex < exercises.Count)
        {
            path = string.IsNullOrWhiteSpace(exercises[selectedExerciseIndex].studentFile) ? DefaultStudentFile : exercises[selectedExerciseIndex].studentFile;
        }

        selectedStudentScript = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
    }

    private void SaveSelectedStudentScript()
    {
        var path = selectedStudentScript == null ? "" : AssetDatabase.GetAssetPath(selectedStudentScript);
        EditorPrefs.SetString(LastStudentScriptKey, path);
    }

    private static MethodInfo FindMainMethod(string scriptPath, string explicitClassName)
    {
        var candidateTypeNames = GetCandidateTypeNames(scriptPath, explicitClassName);
        var candidates = new List<MethodInfo>();

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            var assemblyName = assembly.GetName().Name;
            if (assemblyName != "Assembly-CSharp" && assemblyName != "Assembly-CSharp-firstpass")
            {
                continue;
            }

            foreach (var type in assembly.GetTypes())
            {
                if (candidateTypeNames.Count > 0 && !candidateTypeNames.Contains(type.Name) && !candidateTypeNames.Contains(type.FullName))
                {
                    continue;
                }

                var method = GetValidMainMethod(type);
                if (method != null)
                {
                    candidates.Add(method);
                }
            }
        }

        if (candidates.Count == 1)
        {
            return candidates[0];
        }

        if (!string.IsNullOrWhiteSpace(explicitClassName))
        {
            return candidates.FirstOrDefault(method => method.DeclaringType != null && (method.DeclaringType.Name == explicitClassName || method.DeclaringType.FullName == explicitClassName));
        }

        return null;
    }

    private static HashSet<string> GetCandidateTypeNames(string scriptPath, string explicitClassName)
    {
        var names = new HashSet<string>();
        if (!string.IsNullOrWhiteSpace(explicitClassName))
        {
            names.Add(explicitClassName.Trim());
        }

        if (!File.Exists(scriptPath))
        {
            return names;
        }

        var source = File.ReadAllText(scriptPath);
        var namespaceMatch = Regex.Match(source, @"\bnamespace\s+([A-Za-z_][A-Za-z0-9_.]*)");
        var namespaceName = namespaceMatch.Success ? namespaceMatch.Groups[1].Value : "";
        foreach (Match match in Regex.Matches(source, @"\b(?:class|struct)\s+([A-Za-z_][A-Za-z0-9_]*)"))
        {
            var typeName = match.Groups[1].Value;
            names.Add(typeName);
            if (!string.IsNullOrWhiteSpace(namespaceName))
            {
                names.Add(namespaceName + "." + typeName);
            }
        }

        return names;
    }

    private static MethodInfo GetValidMainMethod(Type type)
    {
        var flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        foreach (var method in type.GetMethods(flags).Where(method => method.Name == "Main"))
        {
            if (method.ReturnType != typeof(object))
            {
                continue;
            }

            var parameters = method.GetParameters();
            if (parameters.Length == 1 && parameters[0].ParameterType == typeof(List<object>))
            {
                return method;
            }
        }

        return null;
    }

    private static Type FindType(string typeName)
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            var type = assembly.GetType(typeName);
            if (type != null)
            {
                return type;
            }
        }

        return null;
    }

    private static Type ResolveSupportedType(string typeName)
    {
        switch ((typeName ?? "").Trim().ToLowerInvariant())
        {
            case "int":
            case "system.int32":
                return typeof(int);
            case "float":
            case "single":
            case "system.single":
                return typeof(float);
            case "double":
            case "system.double":
                return typeof(double);
            case "bool":
            case "boolean":
            case "system.boolean":
                return typeof(bool);
            case "string":
            case "system.string":
                return typeof(string);
            default:
                return null;
        }
    }

    private static object ConvertString(string value, Type targetType)
    {
        if (targetType == typeof(string))
        {
            return value ?? "";
        }

        if (targetType == typeof(int))
        {
            return int.Parse(value, CultureInfo.InvariantCulture);
        }

        if (targetType == typeof(float))
        {
            return float.Parse(value, CultureInfo.InvariantCulture);
        }

        if (targetType == typeof(double))
        {
            return double.Parse(value, CultureInfo.InvariantCulture);
        }

        if (targetType == typeof(bool))
        {
            return bool.Parse(value);
        }

        throw new NotSupportedException("Kiểu dữ liệu chưa được hỗ trợ: " + targetType.Name);
    }

    private static bool ValuesEqual(object expected, object actual, Type valueType)
    {
        if (valueType == typeof(float))
        {
            return Mathf.Abs((float)expected - (float)actual) <= 0.0001f;
        }

        if (valueType == typeof(double))
        {
            return Math.Abs((double)expected - (double)actual) <= 0.0001d;
        }

        return Equals(expected, actual);
    }

    private static string BuildMethodSignature(ExerciseDefinition exercise)
    {
        var parameters = exercise.parameterTypes == null ? "" : string.Join(", ", exercise.parameterTypes);
        return exercise.returnType + " " + exercise.method + "(" + parameters + ")";
    }

    private static string FormatValue(object value)
    {
        if (value == null)
        {
            return "null";
        }

        if (value is string)
        {
            return "\"" + value + "\"";
        }

        if (value is bool)
        {
            return value.ToString().ToLowerInvariant();
        }

        return Convert.ToString(value, CultureInfo.InvariantCulture);
    }

    private static string FormatObjectList(List<object> values)
    {
        if (values == null || values.Count == 0)
        {
            return "[]";
        }

        return "[" + string.Join(", ", values.Select(FormatValue)) + "]";
    }

    private static Color GetStateColor(ResultState state)
    {
        switch (state)
        {
            case ResultState.Pass:
                return new Color(0.73f, 0.95f, 0.76f);
            case ResultState.Fail:
                return new Color(1f, 0.74f, 0.72f);
            case ResultState.Waiting:
                return new Color(1f, 0.9f, 0.55f);
            default:
                return new Color(0.9f, 0.9f, 0.9f);
        }
    }

    private static Color GetStateBorderColor(ResultState state)
    {
        switch (state)
        {
            case ResultState.Pass:
                return new Color(0.17f, 0.58f, 0.25f);
            case ResultState.Fail:
                return new Color(0.75f, 0.15f, 0.13f);
            case ResultState.Waiting:
                return new Color(0.78f, 0.55f, 0.08f);
            default:
                return new Color(0.45f, 0.45f, 0.45f);
        }
    }

    private static Color GetStateTextColor(ResultState state)
    {
        switch (state)
        {
            case ResultState.Pass:
                return new Color(0.05f, 0.28f, 0.1f);
            case ResultState.Fail:
                return new Color(0.42f, 0.03f, 0.03f);
            case ResultState.Waiting:
                return new Color(0.38f, 0.27f, 0.02f);
            default:
                return EditorGUIUtility.isProSkin ? Color.white : Color.black;
        }
    }

    private static void OpenStudentFile(ExerciseDefinition exercise)
    {
        var path = string.IsNullOrWhiteSpace(exercise.studentFile) ? DefaultStudentFile : exercise.studentFile;
        var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
        if (asset != null)
        {
            AssetDatabase.OpenAsset(asset);
            return;
        }

        EditorUtility.DisplayDialog("Không tìm thấy file", "Không tìm thấy file học sinh:\n" + path, "OK");
    }

    private void SetStatus(ResultState state, string message)
    {
        resultState = state;
        statusMessage = message;
        Repaint();
    }

    private enum ResultState
    {
        None,
        Waiting,
        Pass,
        Fail
    }

    [Serializable]
    private sealed class ExerciseFile
    {
        public ExerciseDefinition[] exercises;
    }

    [Serializable]
    private sealed class ExerciseDefinition
    {
        public string id;
        public string title;
        public string description;
        public string testMode;
        public string studentClass;
        public string studentFile;
        public string mainClass;
        public string[] inputTypes;
        public string expectedType;
        public string method;
        public string returnType;
        public string[] parameterTypes;
        public ExerciseTestCase[] testCases;
    }

    [Serializable]
    private sealed class ExerciseTestCase
    {
        public string name;
        public string[] inputValues;
        public string[] args;
        public string expected;
        public string expectedValue;
    }

    private sealed class TestResult
    {
        public string name;
        public bool passed;
        public string message;

        public static TestResult Pass(string name, string message)
        {
            return new TestResult { name = name, passed = true, message = message };
        }

        public static TestResult Fail(string name, string message)
        {
            return new TestResult { name = name, passed = false, message = message };
        }
    }
}
