using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace GameStateStructure
{
	public class DecoratorCollection
	{
		List<IDecorator> m_List = new List<IDecorator>();

		public void Register<T>() where T : IDecorator, new()
		{
			Register(new T());
		}

		public void Register(IDecorator decorator)
		{
			m_List.Add(decorator);
		}

		public void Unregister<T>()
		{
			var removes = m_List.Where(x => x is T).ToArray();
			foreach (var remove in removes)
			{
				m_List.Remove(remove);
			}
			foreach (var remove in removes)
			{
				remove.Dispose();
			}
		}

		public void Unregister(IDecorator decorator)
		{
			m_List.Remove(decorator);
			decorator.Dispose();
		}

		public void Clear()
		{
			var list = m_List.ToArray();
			m_List.Clear();
			foreach (var decorator in list)
			{
				decorator.Dispose();
			}
		}

		IEnumerable<IDecorator> GetDecorators(GameState state)
		{
			foreach (var decorator in m_List)
			{
				yield return decorator;
			}
			foreach (var parent in state.Manager.FindParents<GameState>(state))
			{
				foreach (var decorator in parent.Decorators.m_List)
				{
					yield return decorator;
				}
			}
		}

		internal async Task DoPreInitialize(GameState state)
		{
			foreach (var decorator in GetDecorators(state).OrderBy(x => x.Priority))
			{
				await decorator.DoPreInitialize(state);
			}
		}

		internal async Task DoPostInitialize(GameState state)
		{
			foreach (var decorator in GetDecorators(state).OrderBy(x => x.Priority))
			{
				await decorator.DoPostInitialize(state);
			}
		}

		internal async Task DoPreEnter(GameState state)
		{
			foreach (var decorator in GetDecorators(state).OrderBy(x => x.Priority))
			{
				await decorator.DoPreEnter(state);
			}
		}

		internal void DoPostEnter(GameState state)
		{
			foreach (var decorator in GetDecorators(state).OrderBy(x => x.Priority))
			{
				decorator.DoPostEnter(state);
			}
		}

		internal async Task DoPreExit(GameState state)
		{
			foreach (var decorator in GetDecorators(state).OrderBy(x => x.Priority))
			{
				await decorator.DoPreExit(state);
			}
		}

		internal async Task DoPostExit(GameState state)
		{
			foreach (var decorator in GetDecorators(state).OrderBy(x => x.Priority))
			{
				await decorator.DoPostExit(state);
			}
		}

	}
}