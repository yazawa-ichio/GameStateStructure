using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace GameStateStructure
{
	public class Context
	{
		GameState m_State;
		bool m_Disposed = false;
		List<IDisposable> m_Disposable = new List<IDisposable>();
		List<IExitTask> m_ExitTasks = new List<IExitTask>();
		IDisposable m_Owned;
		CancellationTokenSource m_Cancellation = new CancellationTokenSource();
		bool m_Cancelled;

		internal bool ForceReleased { get; private set; }

		public GameObject ContextObject { get; internal set; }

		public CancellationToken DisposeCancellationToken { get; private set; }

		internal Context(GameState state)
		{
			m_State = state;
			DisposeCancellationToken = m_Cancellation.Token;
		}

		internal void SetOwned(IDisposable owned)
		{
			m_Owned = owned;
		}

		void DisposeOwned(List<Exception> errors)
		{
			var owned = m_Owned;
			m_Owned = null;
			if (owned == null)
			{
				return;
			}
			try
			{
				owned.Dispose();
			}
			catch (Exception ex)
			{
				errors.Add(ex);
			}
		}

		public T Manage<T>(T disposable) where T : IDisposable
		{
			if (m_Disposed)
			{
				disposable?.Dispose();
				return disposable;
			}
			m_Disposable.Add(disposable);
			return disposable;
		}

		public GameObject Manage(GameObject obj)
		{
			if (m_Disposed)
			{
				GameObject.Destroy(obj);
				return obj;
			}
			m_Disposable.Add(new GameObjectDisposer(obj));
			return obj;
		}

		public IDisposable Manage(Action action)
		{
			if (m_Disposed)
			{
				action?.Invoke();
				return EmptyDisposable.Instance;
			}
			var ret = new ActionDisposer(action);
			m_Disposable.Add(ret);
			return ret;
		}

		public Task<T> Manage<T>(Func<CancellationToken, Task<T>> func)
		{
			return func(m_Cancellation.Token);
		}

		public CancellationTokenSource Manage(CancellationTokenSource source)
		{
			m_Cancellation.Token.Register(() =>
			{
				if (!source.IsCancellationRequested)
				{
					source.Cancel();
				}
			});
			return source;
		}

		/// <summary>ステート終了時に await される非同期処理を登録する。</summary>
		public T RegisterExitTask<T>(T task) where T : IExitTask
		{
			if (m_Disposed) throw new ObjectDisposedException(nameof(Context));
			m_ExitTasks.Add(task);
			return task;
		}

		public T ManageComponent<T>(T component) where T : Component
		{
			if (m_Disposed)
			{
				new ComponentDisposer(component).Dispose();
				return component;
			}
			m_Disposable.Add(new ComponentDisposer(component));
			return component;
		}

		public T Unmanage<T>(T disposable) where T : IDisposable
		{
			if (m_Disposed) return disposable;
			m_Disposable.Remove(disposable);
			return disposable;
		}

		public void UnregisterExitTask(IExitTask task)
		{
			if (m_Disposed) return;
			m_ExitTasks.Remove(task);
		}

		internal async Task DisposeAsync(CancellationToken ct = default)
		{
			if (m_Disposed) return;
			m_Disposed = true;
			List<Exception> errors = new List<Exception>();
			DisposeManaged(errors);
			// 待っている間は登録を残す。先に空にすると、止まったまま強制解放に来たときに ForceRelease に倒せない
			var tasks = m_ExitTasks.ToArray();
			try
			{
				await Task.WhenAll(tasks.Select(x => x.Run(ct)));
			}
			catch (Exception ex)
			{
				errors.Add(ex);
			}
			m_ExitTasks.Clear();
			DisposeOwned(errors);
			Cancel(errors);
			Throw(errors);
		}

		internal void ForceDispose()
		{
			ForceReleased = true;
			if (m_Disposed)
			{
				List<Exception> pending = new List<Exception>();
				ForceReleaseExitTasks(pending);
				DisposeOwned(pending);
				Cancel(pending);
				Throw(pending);
				return;
			}
			m_Disposed = true;
			List<Exception> errors = new List<Exception>();
			DisposeManaged(errors);
			ForceReleaseExitTasks(errors);
			DisposeOwned(errors);
			Cancel(errors);
			Throw(errors);
		}

		void ForceReleaseExitTasks(List<Exception> errors)
		{
			var tasks = m_ExitTasks.ToArray();
			m_ExitTasks.Clear();
			foreach (var task in tasks)
			{
				try
				{
					task.ForceRelease();
				}
				catch (Exception ex)
				{
					errors.Add(ex);
				}
			}
		}

		void DisposeManaged(List<Exception> errors)
		{
			foreach (var disposable in m_Disposable)
			{
				try
				{
					disposable?.Dispose();
				}
				catch (Exception ex)
				{
					errors.Add(ex);
				}
			}
			m_Disposable.Clear();
		}

		// 強制解放は解放済みの Context にも来る。二度呼ばれても壊れないこと
		void Cancel(List<Exception> errors)
		{
			if (m_Cancelled)
			{
				return;
			}
			m_Cancelled = true;
			try
			{
				m_Cancellation.Cancel();
			}
			catch (Exception ex)
			{
				errors.Add(ex);
			}
			m_Cancellation.Dispose();
		}

		static void Throw(List<Exception> errors)
		{
			if (errors.Count == 0)
			{
				return;
			}
			if (errors.Count == 1)
			{
				throw errors[0];
			}
			throw new AggregateException(errors);
		}

	}

}