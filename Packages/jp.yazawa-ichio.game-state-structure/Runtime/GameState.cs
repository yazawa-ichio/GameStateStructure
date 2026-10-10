using GameStateStructure.Logger;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace GameStateStructure
{
	public interface IProcess
	{
		Task Run(CancellationToken ct);
	}

	public interface IProcess<TResult>
	{
		Task<TResult> Run(CancellationToken ct);
	}

	public abstract class GameState
	{
		bool m_Active = false;

		public bool Active => m_Active;

		public bool IsRoot => Manager.Root == this;

		public bool IsLeaf => Manager.IsLeaf(this);

		public Context Context { get; internal set; }

		public GameStateManager Manager { get; internal set; }

		/// <summary>自身より下のステートにだけ効く Decorator。</summary>
		protected internal DecoratorCollection DescendantDecorators { get; private set; } = new DecoratorCollection();

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

		protected virtual Task OnInitialize()
		{
			return Task.CompletedTask;
		}

		internal Task DoPreEnter()
		{
			Log.Debug("DoPreEnter {0}", GetType());
			return OnPreEnter();
		}

		protected virtual Task OnPreEnter()
		{
			return Task.CompletedTask;
		}

		internal void DoEnter()
		{
			Log.Debug("DoEnter {0}", GetType());
			m_Active = true;
			OnEnter();
		}

		protected virtual void OnEnter() { }

		internal async Task DoExit()
		{
			m_Active = false;
			Log.Debug("DoExit {0}", GetType());
			Log.Trace("PreExit {0}", GetType());
			if (Context.ForceReleased)
			{
				Log.Debug("Exit skipped (force released) {0}", GetType());
				return;
			}
			await OnPreExit();
			await Context.DisposeAsync();
			if (Context.ForceReleased)
			{
				Log.Debug("Exit skipped (force released) {0}", GetType());
				return;
			}
			Log.Trace("Exit {0}", GetType());
			await OnExit();
		}

		protected virtual Task OnPreExit()
		{
			return Task.CompletedTask;
		}

		protected virtual Task OnExit()
		{
			return Task.CompletedTask;
		}

		/// <summary>子ステートが破棄されたときに呼ばれる。</summary>
		protected internal virtual void OnPopChild(GameState state, PopReason reason) { }

		protected void Pop()
		{
			Manager.Pop(this);
		}

#if GSS_UNITASK && !GSS_DISABLE_UNITASK
		protected void Handle(Func<Cysharp.Threading.Tasks.UniTask> task, bool ignoreCancel = true)
		{
			Manager.Handle(task, ignoreCancel);
		}
#else
		protected void Handle(Func<Task> task, bool ignoreCancel = true)
		{
			Manager.Handle(task, ignoreCancel);
		}
#endif

		protected GameState GetParentState()
		{
			return Manager.FindParent<GameState>(this);
		}

		protected T FindParentState<T>() where T : class
		{
			return Manager.FindParent<T>(this);
		}

		protected IEnumerable<T> FindParentStates<T>() where T : class
		{
			return Manager.FindParents<T>(this);
		}

		internal void DoOnApplicationPause(bool pause)
		{
			if (m_Active)
			{
				OnApplicationPause(pause);
			}
		}

		protected virtual void OnApplicationPause(bool pause) { }

		internal void DoOnApplicationFocus(bool focus)
		{
			if (m_Active)
			{
				OnApplicationFocus(focus);
			}
		}

		protected virtual void OnApplicationFocus(bool focus) { }

	}

}