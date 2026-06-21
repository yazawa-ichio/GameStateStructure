using GameStateStructure.Logger;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace GameStateStructure
{
	class AsyncLock
	{
		class Handle : IDisposable
		{
			AsyncLock m_Owner;
			bool m_Disposed = false;

			public Handle(AsyncLock owner)
			{
				m_Owner = owner;
			}

			public void Dispose()
			{
				if (m_Disposed) return;
				m_Disposed = true;
				m_Owner.DoNext();
				GC.SuppressFinalize(this);
			}
		}

		bool m_IsLocked = false;
		object m_Lock = new object();
		Exception m_Error;
		Queue<TaskCompletionSource<IDisposable>> m_Waiters = new Queue<TaskCompletionSource<IDisposable>>();

		public bool IsLocked
		{
			get
			{
				lock (m_Lock)
				{
					return m_IsLocked;
				}
			}
		}

		public Task<IDisposable> Enter(CancellationToken token)
		{
			token.ThrowIfCancellationRequested();
			lock (m_Lock)
			{
				if (m_Error != null)
				{
					return Task.FromException<IDisposable>(m_Error);
				}
				if (!m_IsLocked)
				{
					m_IsLocked = true;
					return Task.FromResult<IDisposable>(new Handle(this));
				}
				else
				{
					var tcs = new TaskCompletionSource<IDisposable>();
					token.Register(() =>
					{
						tcs.TrySetCanceled(token);
					});
					m_Waiters.Enqueue(tcs);
					return tcs.Task;
				}
			}
		}

		void DoNext()
		{
			lock (m_Lock)
			{
				while (m_Waiters.Count > 0)
				{
					var tsc = m_Waiters.Dequeue();
					if (tsc.Task.IsCompleted)
					{
						continue;
					}
					tsc.TrySetResult(new Handle(this));
					return;
				}
				m_IsLocked = false;
			}
		}

		public void SetError(TransitionHangException error)
		{
			if (!Application.isPlaying)
			{
				Log.Debug($"AsyncLock.SetError called in editor mode, this is not expected. {0}", error);
				m_Error = new OperationCanceledException();
			}
			else
			{
				m_Error = error;
			}
			lock (m_Lock)
			{
				while (m_Waiters.Count > 0)
				{
					var tsc = m_Waiters.Dequeue();
					if (tsc.Task.IsCompleted)
					{
						continue;
					}
					tsc.TrySetException(m_Error);
				}
			}
		}
	}
}