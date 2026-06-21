using System.Collections.Generic;
using UnityEngine.Pool;

namespace GameStateStructure
{
	public class GameStateCollection
	{
		List<GameStateManager> m_List = new(4);

		internal void Add(GameStateManager manager)
		{
			m_List.Add(manager);
		}

		internal void Remove(GameStateManager manager)
		{
			m_List.Remove(manager);
		}

		public IEnumerable<T> GetAllStates<T>() where T : class
		{
			using var _ = ListPool<T>.Get(out var list);
			GetAllStates(list);
			foreach (var item in list)
			{
				yield return item;
			}
		}

		public void GetAllStates<T>(List<T> list) where T : class
		{
			foreach (var manager in m_List)
			{
				manager.FindAllStates(list);
			}
		}
	}

	public partial class GameStateManager
	{
		public static GameStateCollection All { get; } = new GameStateCollection();
	}

}