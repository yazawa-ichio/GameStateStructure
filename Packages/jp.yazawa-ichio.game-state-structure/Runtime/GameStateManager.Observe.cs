using System.Collections.Generic;
using System.Text;
using UnityEngine.Pool;

namespace GameStateStructure
{
	public partial class GameStateManager
	{
		List<IStateObserver> m_Observers;

		/// <summary>観測者を登録する。Entry の再入でも保持される。</summary>
		public void AddObserver(IStateObserver observer)
		{
			if (observer == null)
			{
				return;
			}
			m_Observers ??= new List<IStateObserver>();
			m_Observers.Add(observer);
		}

		public void RemoveObserver(IStateObserver observer)
		{
			m_Observers?.Remove(observer);
		}

		internal void Notify(in StateEvent e)
		{
			if (m_Observers == null)
			{
				return;
			}
			for (var i = 0; i < m_Observers.Count; i++)
			{
				try
				{
					m_Observers[i].OnStateEvent(e);
				}
				catch (System.Exception ex)
				{
					UnityEngine.Debug.LogException(ex);
				}
			}
		}

		/// <summary>親ステート。ルートなら null。</summary>
		public GameState GetParent(GameState state)
		{
			return FindStackData(state)?.Parent?.State;
		}

		/// <summary>直接の子だけを取得する。</summary>
		public void GetChildren(GameState state, List<GameState> list)
		{
			var data = FindStackData(state);
			if (data == null)
			{
				return;
			}
			foreach (var child in data.Children)
			{
				if (child.State != null)
				{
					list.Add(child.State);
				}
			}
		}

		/// <summary>ツリー全体を深さ優先・親が先の順で取得する。</summary>
		public void GetTree(List<StateTreeNode> list)
		{
			Add(m_Data, 0);

			void Add(StackData data, int depth)
			{
				if (data?.State == null)
				{
					return;
				}
				list.Add(new StateTreeNode(data.State, depth, data.Branch));
				foreach (var child in data.Children)
				{
					Add(child, depth + 1);
				}
			}
		}

		public string DumpTree()
		{
			using var _ = ListPool<StateTreeNode>.Get(out var list);
			GetTree(list);

			var builder = new StringBuilder();
			builder.Append(IsBusy ? "[busy] " : "[idle] ");
			builder.Append(name);
			if (list.Count == 0)
			{
				builder.Append(" (no state)");
				return builder.ToString();
			}
			foreach (var node in list)
			{
				builder.AppendLine();
				builder.Append(' ', node.Depth * 2);
				if (node.Branch != null)
				{
					builder.Append('[').Append(node.Branch.Name).Append("] ");
				}
				builder.Append(node.State.GetType().Name);
				if (!node.State.Active)
				{
					builder.Append(" (inactive)");
				}
			}
			return builder.ToString();
		}
	}
}