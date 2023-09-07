using System.Collections.Generic;

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
			foreach (var manager in m_List)
			{
				foreach (var state in manager.GetAllStates())
				{
					if (state is T ret)
					{
						yield return ret;
					}
				}
			}
		}

	}

	public partial class GameStateManager
	{
		public static GameStateCollection All { get; } = new GameStateCollection();
	}

}