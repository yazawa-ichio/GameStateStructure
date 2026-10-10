using GameStateStructure.Diagnostics;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace GameStateStructure.EditorTools
{
	static class TransitionDiagramMenu
	{
		[MenuItem("Window/GameStateStructure/Transition Diagram/クリップボードにコピー")]
		static void CopyToClipboard()
		{
			var text = Generate();
			EditorGUIUtility.systemCopyBuffer = text;
			Debug.Log($"遷移図をクリップボードにコピーしました。\n{text}");
		}

		[MenuItem("Window/GameStateStructure/Transition Diagram/ファイルに保存")]
		static void SaveToFile()
		{
			var path = EditorUtility.SaveFilePanel("遷移図を保存", "", "transitions.md", "md");
			if (string.IsNullOrEmpty(path))
			{
				return;
			}
			File.WriteAllText(path, $"```mermaid\n{Generate()}```\n");
			Debug.Log($"遷移図を保存しました: {path}");
		}

		static string Generate()
		{
			var types = new HashSet<System.Type>();
			foreach (var type in TypeCache.GetTypesWithAttribute<GoToAttribute>())
			{
				types.Add(type);
			}
			foreach (var type in TypeCache.GetTypesWithAttribute<PushAttribute>())
			{
				types.Add(type);
			}
			// インターフェースに属性が付いている場合、宣言型はインターフェースになる。
			// イベント経由の遷移を拾うには実装側の GameState を走査する必要がある
			foreach (var type in TypeCache.GetTypesDerivedFrom<GameState>())
			{
				if (type.IsAbstract)
				{
					continue;
				}
				foreach (var entry in EventCatalog.Get(type))
				{
					if (entry.Transition != null)
					{
						types.Add(type);
						break;
					}
				}
			}
			return TransitionDiagram.GenerateMermaid(types.OrderBy(x => x.FullName, System.StringComparer.Ordinal));
		}
	}
}