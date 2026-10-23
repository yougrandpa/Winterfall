using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace MyAiWorld.EditorTools
{
    // Editor-only visual-review helper. It selects a fixed Game View resolution
    // without touching player settings or the built game.
    public static class ReviewGameView
    {
        public static EditorWindow FindGameView()
        {
            Type gameViewType = Type.GetType("UnityEditor.GameView,UnityEditor");
            if (gameViewType == null)
            {
                return null;
            }

            EditorWindow focused = EditorWindow.focusedWindow;
            if (focused != null && focused.GetType() == gameViewType)
            {
                return focused;
            }

            EditorWindow best = null;
            float bestArea = -1f;
            foreach (UnityEngine.Object candidate in Resources.FindObjectsOfTypeAll(gameViewType))
            {
                EditorWindow window = candidate as EditorWindow;
                if (window == null)
                {
                    continue;
                }

                float area = window.position.width * window.position.height;
                if (area > bestArea)
                {
                    bestArea = area;
                    best = window;
                }
            }

            return best;
        }

        public static bool TrySetResolution(int width, int height)
        {
            try
            {
                Type gameViewType = Type.GetType("UnityEditor.GameView,UnityEditor");
                Type sizesType = Type.GetType("UnityEditor.GameViewSizes,UnityEditor");
                Type sizeType = Type.GetType("UnityEditor.GameViewSize,UnityEditor");
                if (gameViewType == null || sizesType == null || sizeType == null)
                {
                    Debug.LogWarning("ReviewGameView: Game View types are unavailable.");
                    return false;
                }

                EditorWindow gameView = FindGameView() ?? EditorWindow.GetWindow(gameViewType);
                gameView.Focus();

                object sizes = GetSizesInstance(sizesType);
                if (sizes == null)
                {
                    Debug.LogWarning("ReviewGameView: GameViewSizes.instance was not found.");
                    return false;
                }

                object group = GetGroup(sizes, sizesType);
                if (group == null)
                {
                    Debug.LogWarning("ReviewGameView: Game View size group was not found.");
                    return false;
                }

                int index = FindOrAddSize(group, sizeType, width, height);
                if (index < 0)
                {
                    Debug.LogWarning("ReviewGameView: Could not add a " + width + "x" + height + " size.");
                    return false;
                }

                MethodInfo select = gameViewType.GetMethod(
                    "SizeSelectionCallback",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (select == null)
                {
                    Debug.LogWarning("ReviewGameView: SizeSelectionCallback was not found.");
                    return false;
                }

                select.Invoke(gameView, new object[] { index, null });
                FitWindow(gameView, width, height);
                gameView.Repaint();
                Debug.Log("ReviewGameView: requested " + width + "x" + height + " (index " + index +
                          "), target=" + gameViewType.GetProperty("targetRenderSize")?.GetValue(gameView) +
                          ", window=" + gameView.position + ", screen=" + Screen.width + "x" + Screen.height + ".");
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogWarning("ReviewGameView failed: " + exception);
                return false;
            }
        }

        private static void FitWindow(EditorWindow gameView, int width, int height)
        {
            float pixelsPerPoint = EditorGUIUtility.pixelsPerPoint;
            const float toolbarHeight = 24f;
            gameView.position = new Rect(
                gameView.position.x,
                gameView.position.y,
                width / pixelsPerPoint,
                height / pixelsPerPoint + toolbarHeight);
            gameView.Focus();
        }

        private static object GetSizesInstance(Type sizesType)
        {
            Type singletonType = typeof(ScriptableSingleton<>).MakeGenericType(sizesType);
            PropertyInfo instance = singletonType.GetProperty(
                "instance",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (instance != null)
            {
                return instance.GetValue(null);
            }

            PropertyInfo fallback = sizesType.GetProperty(
                "instance",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy);
            return fallback != null ? fallback.GetValue(null) : null;
        }

        private static object GetGroup(object sizes, Type sizesType)
        {
            Type groupEnum = sizesType.GetNestedType("GameViewSizeGroupType") ??
                             Type.GetType("UnityEditor.GameViewSizeGroupType,UnityEditor");

            object standalone = groupEnum != null ? Enum.Parse(groupEnum, "Standalone") : (object)0;
            MethodInfo getGroup = sizesType.GetMethod(
                "GetGroup",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (getGroup != null)
            {
                return getGroup.Invoke(sizes, new[] { standalone });
            }

            // Older editors expose the groups as properties instead.
            PropertyInfo groups = sizesType.GetProperty(
                "currentGroup",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return groups != null ? groups.GetValue(sizes) : null;
        }

        private static int FindOrAddSize(object group, Type sizeType, int width, int height)
        {
            Type groupType = group.GetType();
            MethodInfo countMethod = groupType.GetMethod("GetTotalCount", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            MethodInfo getSize = groupType.GetMethod("GetGameViewSize", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (countMethod == null || getSize == null)
            {
                return -1;
            }

            int count = (int)countMethod.Invoke(group, null);
            for (int i = 0; i < count; i++)
            {
                object size = getSize.Invoke(group, new object[] { i });
                if (size == null)
                {
                    continue;
                }

                Type type = size.GetType();
                PropertyInfo widthProperty = type.GetProperty("width", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                PropertyInfo heightProperty = type.GetProperty("height", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (widthProperty == null || heightProperty == null)
                {
                    continue;
                }

                if ((int)widthProperty.GetValue(size) == width && (int)heightProperty.GetValue(size) == height)
                {
                    return i;
                }
            }

            ConstructorInfo constructor = sizeType.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(candidate => candidate.GetParameters().Length == 4);
            MethodInfo add = groupType.GetMethod("AddCustomSize", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (constructor == null || add == null)
            {
                return -1;
            }

            ParameterInfo[] parameters = constructor.GetParameters();
            var arguments = new object[parameters.Length];
            for (int i = 0; i < parameters.Length; i++)
            {
                Type parameter = parameters[i].ParameterType;
                if (parameter.IsEnum)
                {
                    arguments[i] = Enum.ToObject(parameter, 1);
                }
                else if (parameter == typeof(int))
                {
                    arguments[i] = i == 1 ? width : i == 2 ? height : 0;
                }
                else if (parameter == typeof(string))
                {
                    arguments[i] = width + "x" + height;
                }
                else
                {
                    arguments[i] = parameter.IsValueType ? Activator.CreateInstance(parameter) : null;
                }
            }

            object created = constructor.Invoke(arguments);
            add.Invoke(group, new[] { created });
            return count;
        }
    }
}
