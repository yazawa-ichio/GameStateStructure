using GameStateStructure.Logger;
using System.Threading.Tasks;

namespace GameStateStructure
{
	public interface IModule
	{
		Task Run();
	}

	public interface IModule<TResult>
	{
		Task<TResult> Run();
	}

	public abstract class GameState
	{
		bool m_Active = false;

		public Context Context { get; internal set; }

		public GameStateManager Manager { get; internal set; }

		internal void Setup(GameStateManager manager, Context context)
		{
			Manager = manager;
			Context = context;
		}

		internal void DoUpdate()
		{
			if (m_Active)
			{
				Update();
			}
		}

		protected virtual void Update() { }

		internal Task DoPrepare()
		{
			Log.Debug("DoPrepare {0}", GetType());
			return Prepare();
		}

		protected virtual Task Prepare() => Task.CompletedTask;

		internal void DoEnter()
		{
			Log.Debug("DoEnter {0}", GetType());
			m_Active = true;
			Enter();
		}

		protected virtual void Enter() { }

		internal async Task DoExit()
		{
			Log.Debug("DoExit {0}", GetType());
			Log.Trace("PreExit {0}", GetType());
			await PreExit();
			await Context.DisposeAsync();
			Log.Trace("Exit {0}", GetType());
			await Exit();
		}

		protected virtual Task PreExit() => Task.CompletedTask;

		protected virtual Task Exit() => Task.CompletedTask;

		protected void Pop()
		{
			Manager.Pop(this);
		}
	}

}