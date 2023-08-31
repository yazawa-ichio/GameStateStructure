using GameStateStructure.Logger;
using System;
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

		protected internal DecoratorCollection Decorators { get; private set; } = new DecoratorCollection();

		internal void Setup(GameStateManager manager, Context context)
		{
			Manager = manager;
			Context = context;
		}

		internal void DoUpdate()
		{
			if (m_Active)
			{
				OnUpdate();
			}
		}

		protected virtual void OnUpdate() { }

		internal Task DoInitialize()
		{
			Log.Debug("DoInitialize {0}", GetType());
			return OnInitialize();
		}

		protected virtual Task OnInitialize() => Task.CompletedTask;

		internal void DoEnter()
		{
			Log.Debug("DoEnter {0}", GetType());
			m_Active = true;
			OnEnter();
		}

		protected virtual void OnEnter() { }

		internal async Task DoExit()
		{
			Log.Debug("DoExit {0}", GetType());
			Log.Trace("PreExit {0}", GetType());
			await OnPreExit();
			await Context.DisposeAsync();
			Log.Trace("Exit {0}", GetType());
			await OnExit();
		}

		protected virtual Task OnPreExit() => Task.CompletedTask;

		protected virtual Task OnExit() => Task.CompletedTask;

		internal void DoPop(GameState state)
		{
			OnPop(state);
		}

		protected virtual void OnPop(GameState state) { }

		protected void Pop()
		{
			Manager.Pop(this);
		}

		protected void Handle(Func<Task> task)
		{
			Manager.Handle(task);
		}

	}

}