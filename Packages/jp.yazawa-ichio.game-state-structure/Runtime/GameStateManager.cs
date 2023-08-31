using GameStateStructure.Logger;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace GameStateStructure
{
	public interface IActivator
	{
		T Create<T>();
	}

	public interface IErrorHandler
	{
		void Handle(Exception ex);
	}

	public class Config
	{
		public IActivator Activator { get; set; }

		public IErrorHandler ErrorHandler { get; set; }

		public DecoratorCollection Decorator { get; private set; } = new DecoratorCollection();
	}

	public class InitializeCancelException : Exception
	{
		public InitializeCancelException() { }
	}

	public class GameStateManager : MonoBehaviour
	{
		class StackData
		{
			public CancellationTokenSource CancellationTokenSource = new CancellationTokenSource();

			public StackData Parent;

			public GameState State;

			public List<StackData> Children = new List<StackData>();

			public void Update()
			{
				State?.DoUpdate();
				foreach (var child in Children)
				{
					child.Update();
				}
			}
		}

		StackData m_Data;
		Config m_Config;
		AsyncLock m_AsyncLock = new AsyncLock();

		public IActivator Activator { get; set; }

		public IErrorHandler ErrorHandler { get; set; }

		public DecoratorCollection Decorators { get; private set; }


		T CreateState<T>(ParameterHolder parameter) where T : GameState
		{
			var state = New();
			var context = new Context(state);
			var obj = new GameObject(typeof(T).Name);
			obj.transform.SetParent(transform);
			context.Root = context.Manage(obj);
			state.Setup(this, context);
			parameter.Apply(state);
			return state;

			T New()
			{
				var activator = m_Config?.Activator;
				if (activator != null)
				{
					return activator.Create<T>();
				}
				return System.Activator.CreateInstance<T>();
			}
		}

		async Task ExitAll(StackData data, bool remove)
		{
			if (data == null)
			{
				return;
			}
			data.CancellationTokenSource.Cancel();
			foreach (var child in data.Children.ToArray())
			{
				await ExitAll(child, true);
			}
			if (remove)
			{
				data.Parent?.Children.Remove(data);
			}
			var state = data.State;
			data.State = null;
			await Decorators.DoPreExit(state);
			await state.DoExit();
			await Decorators.DoPostExit(state);
			data.Parent?.State?.DoPop(state);
		}

		public Task Entry<T>() where T : GameState
		{
			return Entry<T>(new());
		}

		public async Task Entry<T>(Config config) where T : GameState
		{
			Activator = config.Activator;
			ErrorHandler = config.ErrorHandler;
			Decorators = config.Decorator;

			await ExitAll(m_Data, remove: true);
			m_Config = config ?? new();
			var data = new StackData();
			var state = CreateState<T>(new ParameterHolder());
			data.State = state;
			m_Data = new StackData();
			m_Data.State = state;
			try
			{
				await Decorators.DoPreInitialize(state);
				await state.DoInitialize();
				await Decorators.DoPostInitialize(state);
				await Decorators.DoPreEnter(state);
				state.DoEnter();
				Decorators.DoPostEnter(state);
			}
			catch (Exception e)
			{
				await Decorators.OnError(state, e);
				throw;
			}
		}

		internal async void Handle(Func<Task> task)
		{
			try
			{
				await task();
			}
			catch (InitializeCancelException)
			{
				return;
			}
			catch (OperationCanceledException)
			{
				return;
			}
			catch (Exception e)
			{
				var handle = ErrorHandler;
				if (handle != null)
				{
					handle.Handle(e);
				}
				else
				{
					Debug.LogException(e);
				}
			}
		}

		public void GoTo<T>(GameState current, ParameterHolder parameter) where T : GameState, new()
		{
			Handle(async () =>
			{
				using var _ = await m_AsyncLock.Enter(CancellationToken.None);
				var data = FindStackData(current);
				if (data == null)
				{
					return;
				}
				var state = CreateState<T>(parameter);
				try
				{
					await Decorators.DoPreInitialize(state);
					await state.DoInitialize();
					await Decorators.DoPostInitialize(state);
					await ExitAll(data, remove: false);
					data.State = state;
					await Decorators.DoPreExit(current);
					await current.DoExit();
					await Decorators.DoPostExit(current);
					await Decorators.DoPreEnter(state);
					state.DoEnter();
					Decorators.DoPostEnter(state);
				}
				catch (Exception e)
				{
					await Decorators.OnError(state, e);
					throw;
				}
			});
		}

		public void Push<T>(GameState current, ParameterHolder parameter, CancellationToken token) where T : GameState, new()
		{
			Log.Debug("{0}.Push<{1}>", current, typeof(T));
			Handle(async () =>
			{
				using var _ = await m_AsyncLock.Enter(token);
				var data = FindStackData(current);
				if (data == null)
				{
					return;
				}
				var state = CreateState<T>(parameter);
				try
				{
					await Decorators.DoPreInitialize(state);
					await state.DoInitialize();
					await Decorators.DoPostInitialize(state);
					var child = new StackData();
					child.State = state;
					child.Parent = data;
					data.Children.Add(child);
					await Decorators.DoPreEnter(state);
					state.DoEnter();
					Decorators.DoPostEnter(state);
				}
				catch (Exception e)
				{
					await Decorators.OnError(state, e);
					throw;
				}
			});
		}

		public void Pop(GameState current)
		{
			Handle(async () =>
			{
				using var _ = await m_AsyncLock.Enter(CancellationToken.None);
				var data = FindStackData(current);
				if (data == null)
				{
					return;
				}
				await ExitAll(data, true);
			});
		}

		public async Task<TResult> Module<TGameState, TResult>(GameState current, ParameterHolder parameter, CancellationToken token) where TGameState : GameState, IModule<TResult>
		{
			using var _ = await m_AsyncLock.Enter(token);

			var data = FindStackData(current);
			if (data == null)
			{
				throw new InvalidOperationException("not current state");
			}
			var state = CreateState<TGameState>(parameter);
			try
			{
				await Decorators.DoPreInitialize(state);
				await state.DoInitialize();
				await Decorators.DoPostInitialize(state);
				var child = new StackData();
				child.State = state;
				child.Parent = data;
				data.Children.Add(child);
				await Decorators.DoPreEnter(state);
				state.DoEnter();
				Decorators.DoPostEnter(state);
				var ret = await state.Run();
				await ExitAll(child, true);
				return ret;
			}
			catch (Exception e)
			{
				await Decorators.OnError(state, e);
				throw;
			}
		}

		public async Task Module<TGameState>(GameState current, ParameterHolder parameter, CancellationToken token) where TGameState : GameState, IModule
		{
			using var _ = await m_AsyncLock.Enter(token);

			var data = FindStackData(current);
			if (data == null)
			{
				throw new InvalidOperationException("not current state");
			}
			var state = CreateState<TGameState>(parameter);
			try
			{
				await Decorators.DoPreInitialize(state);
				await state.DoInitialize();
				await Decorators.DoPostInitialize(state);
				var child = new StackData();
				child.State = state;
				child.Parent = data;
				data.Children.Add(child);
				await Decorators.DoPreEnter(state);
				state.DoEnter();
				Decorators.DoPostEnter(state);
				await state.Run();
				await ExitAll(child, true);
			}
			catch (Exception e)
			{
				await Decorators.OnError(state, e);
				throw;
			}
		}

		void Update()
		{
			m_Data?.Update();
		}

		StackData FindStackData(GameState state)
		{
			if (m_Data == null)
			{
				return null;
			}
			return Find(state, m_Data);

			StackData Find(GameState state, StackData data)
			{
				if (state == data.State)
				{
					return data;
				}
				foreach (var chind in data.Children)
				{
					var ret = Find(state, chind);
					if (ret != null)
					{
						return ret;
					}
				}
				return null;
			}
		}

		public T GetParent<T>(GameState state) where T : GameState
		{
			var data = FindStackData(state)?.Parent;
			while (data != null)
			{
				if (data.State is T ret)
				{
					return ret;
				}
				data = data.Parent;
			}
			return default;
		}

		public IEnumerable<T> GetParents<T>(GameState state) where T : GameState
		{
			var data = FindStackData(state)?.Parent;
			while (data != null)
			{
				if (data.State is T ret)
				{
					yield return ret;
				}
				data = data.Parent;
			}
		}

		IEnumerable<GameState> GetGameStates()
		{
			return GetImpl(m_Data);

			IEnumerable<GameState> GetImpl(StackData data)
			{
				if (data != null)
				{
					yield break;
				}
				foreach (var child in data.Children)
				{
					foreach (var state in GetImpl(child))
					{
						yield return state;
					}
				}
				if (data.State != null)
				{
					yield return data.State;
				}
			}
		}

		IEnumerable<DecoratorCollection> GetDecorators(StackData data)
		{
			if (data == null)
			{
				yield break;
			}
		}


	}

}