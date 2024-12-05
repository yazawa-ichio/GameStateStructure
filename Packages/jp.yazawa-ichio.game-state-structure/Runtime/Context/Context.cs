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
		List<IAsyncDisposable> m_AsnycDisposable = new List<IAsyncDisposable>();
		CancellationTokenSource m_Cancellation = new CancellationTokenSource();

		public GameObject Root { get; internal set; }

		public CancellationToken DisposeCancellationToken => m_Cancellation.Token;

		internal Context(GameState state)
		{
			m_State = state;
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
				return null;
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

		public T ManageAsync<T>(T disposable) where T : IAsyncDisposable
		{
			if (m_Disposed) throw new ObjectDisposedException(nameof(Context));
			m_AsnycDisposable.Add(disposable);
			return disposable;
		}

		public T ManageComponent<T>(T component) where T : Component
		{
			if (m_Disposed) throw new ObjectDisposedException(nameof(Context));
			m_Disposable.Add(new ComponentDisposer(component));
			return component;
		}

		public T Unmanage<T>(T disposable) where T : IDisposable
		{
			if (m_Disposed) return disposable;
			m_Disposable.Remove(disposable);
			return disposable;
		}

		public void Unmanage(IAsyncDisposable disposable)
		{
			if (m_Disposed) return;
			m_AsnycDisposable.Remove(disposable);
		}

		internal async Task DisposeAsync()
		{
			if (m_Disposed) return;
			m_Disposed = true;
			List<Exception> errors = new List<Exception>();
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
			try
			{
				await Task.WhenAll(m_AsnycDisposable.Select(async x => await x.DisposeAsync()));
			}
			catch (Exception ex)
			{
				errors.Add(ex);
			}
			try
			{
				m_Cancellation.Cancel();
			}
			catch (Exception ex)
			{
				errors.Add(ex);
			}
			m_Cancellation.Dispose();
			if (errors.Count > 0)
			{
				throw errors[0];
			}
		}

	}

}