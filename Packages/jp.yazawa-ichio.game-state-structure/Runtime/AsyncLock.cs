using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

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
		Queue<TaskCompletionSource<IDisposable>> m_Waiters = new Queue<TaskCompletionSource<IDisposable>>();

		public Task<IDisposable> Enter(CancellationToken token)
		{
			token.ThrowIfCancellationRequested();
			lock (m_Lock)
			{
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

	}
}