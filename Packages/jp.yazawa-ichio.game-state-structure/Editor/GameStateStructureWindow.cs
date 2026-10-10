using GameStateStructure.Diagnostics;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace GameStateStructure.EditorTools
{
	/// <summary>実行中のステートツリーを表示する EditorWindow。</summary>
	public class GameStateStructureWindow : EditorWindow, IStateObserver
	{
		const int MaxLogCount = 200;

		[MenuItem("Window/GameStateStructure/State Viewer")]
		static void Open()
		{
			GetWindow<GameStateStructureWindow>("GameState");
		}

		readonly List<GameStateManager> m_Managers = new();
		readonly List<StateTreeNode> m_Nodes = new();
		readonly List<string> m_Log = new();
		readonly HashSet<GameStateManager> m_Observed = new();
		readonly HashSet<GameState> m_Expanded = new();
		readonly Dictionary<(GameState, string), object[]> m_Args = new();

		Vector2 m_TreeScroll;
		Vector2 m_LogScroll;
		bool m_ShowLog = true;

		// OnGUI の途中で遷移するとツリーの列挙が壊れるため、描画後に実行する
		GameState m_PendingState;
		EventEntry m_PendingEvent;
		object[] m_PendingArgs;

		void OnEnable()
		{
			EditorApplication.update += OnEditorUpdate;
		}

		void OnDisable()
		{
			EditorApplication.update -= OnEditorUpdate;
			Detach();
		}

		void OnEditorUpdate()
		{
			if (!Application.isPlaying)
			{
				if (m_Observed.Count > 0)
				{
					Detach();
					m_Log.Clear();
					m_Args.Clear();
					m_Expanded.Clear();
				}
				return;
			}
			Refresh();
			Repaint();
		}

		void Refresh()
		{
			m_Managers.Clear();
			GameStateManager.All.GetManagers(m_Managers);
			foreach (var manager in m_Managers)
			{
				if (manager != null && m_Observed.Add(manager))
				{
					manager.AddObserver(this);
				}
			}
			m_Observed.RemoveWhere(x => x == null);
		}

		void Detach()
		{
			foreach (var manager in m_Observed)
			{
				if (manager != null)
				{
					manager.RemoveObserver(this);
				}
			}
			m_Observed.Clear();
			m_Managers.Clear();
		}

		public void OnStateEvent(in StateEvent e)
		{
			m_Log.Add($"{Time.frameCount,7}  {e}");
			if (m_Log.Count > MaxLogCount)
			{
				m_Log.RemoveRange(0, m_Log.Count - MaxLogCount);
			}
		}

		void OnGUI()
		{
			if (!Application.isPlaying)
			{
				EditorGUILayout.HelpBox("再生中のみステートを表示します。", MessageType.Info);
				return;
			}
			if (m_Managers.Count == 0)
			{
				EditorGUILayout.HelpBox("GameStateManager が見つかりません。", MessageType.Info);
				return;
			}

			DrawTree();
			DrawLog();
			InvokePending();
		}

		void DrawTree()
		{
			m_TreeScroll = EditorGUILayout.BeginScrollView(m_TreeScroll);
			foreach (var manager in m_Managers)
			{
				if (manager == null)
				{
					continue;
				}
				EditorGUILayout.LabelField(
					$"{manager.name}   {(manager.IsBusy ? "[busy]" : "[idle]")}",
					EditorStyles.boldLabel);

				m_Nodes.Clear();
				manager.GetTree(m_Nodes);
				if (m_Nodes.Count == 0)
				{
					EditorGUILayout.LabelField("    (no state)");
					continue;
				}
				foreach (var node in m_Nodes)
				{
					DrawNode(node);
				}
				EditorGUILayout.Space();
			}
			EditorGUILayout.EndScrollView();
		}

		void DrawNode(StateTreeNode node)
		{
			var state = node.State;
			var events = EventCatalog.Get(state);
			var indent = node.Depth * 16f;

			using (new EditorGUILayout.HorizontalScope())
			{
				GUILayout.Space(indent);

				if (events.Count > 0)
				{
					var expanded = m_Expanded.Contains(state);
					if (EditorGUILayout.Foldout(expanded, GUIContent.none, true, EditorStyles.foldout) != expanded)
					{
						if (expanded)
						{
							m_Expanded.Remove(state);
						}
						else
						{
							m_Expanded.Add(state);
						}
					}
					GUILayout.Space(-30f);
				}

				var label = node.Branch == null
					? state.GetType().Name
					: $"[{node.Branch.Name}] {state.GetType().Name}";
				if (!state.Active)
				{
					label += "  (inactive)";
				}
				if (events.Count > 0)
				{
					label += $"   [Event x{events.Count}]";
				}

				var color = GUI.color;
				if (!state.Active)
				{
					GUI.color = new Color(color.r, color.g, color.b, 0.5f);
				}
				if (GUILayout.Button(label, EditorStyles.label))
				{
					var obj = state.Context?.ContextObject;
					if (obj != null)
					{
						Selection.activeGameObject = obj;
						EditorGUIUtility.PingObject(obj);
					}
				}
				GUI.color = color;
			}

			if (events.Count > 0 && m_Expanded.Contains(state))
			{
				DrawEvents(state, events, indent + 20f);
			}
		}

		void DrawEvents(GameState state, IReadOnlyList<EventEntry> events, float indent)
		{
			foreach (var entry in events)
			{
				using (new EditorGUILayout.HorizontalScope())
				{
					GUILayout.Space(indent);
					using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
					{
						DrawEvent(state, entry);
					}
				}
			}
		}

		void DrawEvent(GameState state, EventEntry entry)
		{
			var title = entry.Label;
			if (entry.Transition != null)
			{
				title += $"  →  {entry.Transition.Name}";
			}

			if (!entry.CanInvoke)
			{
				EditorGUILayout.LabelField(title, EditorStyles.miniBoldLabel);
				EditorGUILayout.HelpBox(
					"ツールから値を用意できない引数があるため実行できません。"
					+ "厳密に扱う場合は GSS_STRICT_EVENT を定義してください。",
					MessageType.None);
				return;
			}

			var args = GetArgs(state, entry);
			for (var i = 0; i < entry.Parameters.Length; i++)
			{
				args[i] = DrawParameter(entry.Parameters[i], args[i]);
			}

			using (new EditorGUI.DisabledScope(!state.Active))
			{
				if (GUILayout.Button(title))
				{
					m_PendingState = state;
					m_PendingEvent = entry;
					m_PendingArgs = entry.Parameters.Length == 0 ? null : (object[])args.Clone();
				}
			}
		}

		object[] GetArgs(GameState state, EventEntry entry)
		{
			var key = (state, entry.Label);
			if (m_Args.TryGetValue(key, out var args) && args.Length == entry.Parameters.Length)
			{
				return args;
			}
			args = new object[entry.Parameters.Length];
			for (var i = 0; i < args.Length; i++)
			{
				args[i] = EventCatalog.CreateDefault(entry.Parameters[i].ParameterType);
			}
			m_Args[key] = args;
			return args;
		}

		static object DrawParameter(ParameterInfo parameter, object value)
		{
			var type = parameter.ParameterType;
			if (type == typeof(bool))
			{
				return EditorGUILayout.Toggle(parameter.Name, (bool)value);
			}
			if (type == typeof(int))
			{
				return EditorGUILayout.IntField(parameter.Name, (int)value);
			}
			if (type == typeof(float))
			{
				return EditorGUILayout.FloatField(parameter.Name, (float)value);
			}
			if (type == typeof(string))
			{
				return EditorGUILayout.TextField(parameter.Name, (string)value ?? string.Empty);
			}
			if (type.IsEnum)
			{
				return EditorGUILayout.EnumPopup(parameter.Name, (Enum)value);
			}
			return value;
		}

		void InvokePending()
		{
			if (m_PendingState == null)
			{
				return;
			}
			var state = m_PendingState;
			var entry = m_PendingEvent;
			var args = m_PendingArgs;
			m_PendingState = null;
			m_PendingArgs = null;

			try
			{
				EventCatalog.Invoke(state, entry, args);
			}
			catch (Exception e)
			{
				Debug.LogException(e);
			}
		}

		void DrawLog()
		{
			using (new EditorGUILayout.HorizontalScope())
			{
				m_ShowLog = EditorGUILayout.Foldout(m_ShowLog, $"遷移ログ ({m_Log.Count})", true);
				if (GUILayout.Button("Clear", EditorStyles.miniButton, GUILayout.Width(60f)))
				{
					m_Log.Clear();
				}
			}
			if (!m_ShowLog)
			{
				return;
			}
			m_LogScroll = EditorGUILayout.BeginScrollView(m_LogScroll, GUILayout.MinHeight(120f));
			for (var i = m_Log.Count - 1; i >= 0; i--)
			{
				EditorGUILayout.LabelField(m_Log[i], EditorStyles.miniLabel);
			}
			EditorGUILayout.EndScrollView();
		}
	}
}