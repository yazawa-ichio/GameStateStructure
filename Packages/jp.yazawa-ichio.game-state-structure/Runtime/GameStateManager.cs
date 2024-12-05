using GameStateStructure.Logger;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace GameStateStructure
{

	public class Config
	{
		public IActivator Activator { get; set; }

		public IErrorHandler ErrorHandler { get; set; }

		public DecoratorCollection Decorator { get; private set; } = new DecoratorCollection();
	}

	public partial class GameStateManager : MonoBehaviour
	{
		class StackData
		{
			public CancellationTokenSource CancellationTokenSource = new();

			public StackData Parent;

			public GameState State;

			public List<StackData> Children = new();

			public void Update()
			{
				State?.DoUpdate();
				foreach (var child in Children)
				{
					child.Update();
				}
			}

			public void PreCancel()
			{
				CancellationTokenSource?.Cancel();
				CancellationTokenSource = null;
				foreach (var child in Children)
				{
					child.PreCancel();
				}
			}
		}

		StackData m_Data;
		AsyncLock m_AsyncLock = new();

		public IActivator Activator { get; set; }

		public IErrorHandler ErrorHandler { get; set; }

		public DecoratorCollection Decorators { get; private set; }

		private void Awake()
		{
			All.Add(this);
		}

		private void OnDestroy()
		{
			All.Remove(this);
		}

		private void OnApplicationPause(bool pause)
		{
			foreach (var statet in FindAllStates<GameState>())
			{
				statet.DoOnApplicationPause(pause);
			}
		}

		private void OnApplicationFocus(bool focus)
		{
			foreach (var statet in FindAllStates<GameState>())
			{
				statet.DoOnApplicationFocus(focus);
			}
		}

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
				var activator = Activator;
				if (activator != null)
				{
					return activator.Create<T>();
				}
				return System.Activator.CreateInstance<T>();
			}
		}

		async Task ExitAll(StackData data)
		{
			if (data == null)
			{
				return;
			}
			data.PreCancel();
			foreach (var child in data.Children.ToArray())
			{
				await ExitAll(child);
			}
			data.Parent?.Children.Remove(data);
			var state = data.State;
			data.State = null;
			await Decorators.DoPreExit(state);
			await state.DoExit();
			await Decorators.DoPostExit(state);
			Log.Debug("DoPop {0}", state);
			data.Parent?.State?.DoPop(state);
		}

		public Task Entry<T>() where T : GameState
		{
			return Entry<T>(new(), new());
		}

		public async Task Entry<T>(ParameterHolder parameter, Config config) where T : GameState
		{
			Activator = config.Activator;
			ErrorHandler = config.ErrorHandler;
			Decorators = config.Decorator;
			m_AsyncLock?.SetError(new TransitionHangException());
			m_AsyncLock = new AsyncLock();

			await ExitAll(m_Data);
			var data = new StackData();
			var state = CreateState<T>(parameter);
			data.State = state;
			m_Data = new StackData();
			m_Data.State = state;
			try
			{
				await Decorators.DoPreInitialize(state);
				await state.DoInitialize();
				await Decorators.DoPostInitialize(state);
				await state.DoPreEnter();
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
				Log.Debug("Cancel");
				return;
			}
			catch (OperationCanceledException)
			{
				Log.Debug("Cancel");
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
				m_AsyncLock?.SetError(new(e));
			}
		}

		public void GoTo<T>(GameState current, ParameterHolder parameter) where T : GameState, new()
		{
			Handle(async () =>
			{
				FindStackData(current)?.PreCancel();

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
					var parent = data.Parent;
					await ExitAll(data);
					var newData = new StackData();
					newData.State = state;
					if (parent == null)
					{
						parent.Children.Add(newData);
					}
					else
					{
						m_Data = newData;
					}
					await Decorators.DoPreExit(current);
					await current.DoExit();
					await Decorators.DoPostExit(current);
					await state.DoPreEnter();
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

		public void Push<T>(GameState current, ParameterHolder parameter) where T : GameState, new()
		{
			Log.Debug("{0}.Push<{1}>", current, typeof(T));
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
					var child = new StackData();
					child.State = state;
					child.Parent = data;
					data.Children.Add(child);
					await state.DoPreEnter();
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
				FindStackData(current)?.PreCancel();
				using var _ = await m_AsyncLock.Enter(CancellationToken.None);
				var data = FindStackData(current);
				if (data == null)
				{
					return;
				}
				await ExitAll(data);
			});
		}

		public async Task<TResult> RunProcess<TGameState, TResult>(GameState current, ParameterHolder parameter, CancellationToken token) where TGameState : GameState, IProcess<TResult>
		{
			using var _ = await m_AsyncLock.Enter(token);

			(StackData child, TGameState state) = await ProcessPush<TGameState>(current, parameter, token);

			using var cts = CancellationTokenSource.CreateLinkedTokenSource(token, child.CancellationTokenSource.Token);
			token = cts.Token;

			Log.Debug("{0}.Process<{1}, {2}> Run Start", current, typeof(TGameState), typeof(TResult));
			TResult result = default;
			try
			{
				result = await state.Run(token);
			}
			catch
			{
				await HandleExitAll(child, token);
				throw;
			}
			Log.Debug("{0}.Process<{1}, {2}> Run Result {3}", current, typeof(TGameState), typeof(TResult), result);
			await HandleExitAll(child, token);
			return result;
		}

		public async Task RunProcess<TGameState>(GameState current, ParameterHolder parameter, CancellationToken token) where TGameState : GameState, IProcess
		{
			using var _ = await m_AsyncLock.Enter(token);

			(StackData child, TGameState state) = await ProcessPush<TGameState>(current, parameter, token);

			using var cts = CancellationTokenSource.CreateLinkedTokenSource(token, child.CancellationTokenSource.Token);
			token = cts.Token;

			Log.Debug("{0}.Process<{1}> Run Start", current, typeof(TGameState));
			try
			{
				await state.Run(token);
			}
			catch
			{
				await HandleExitAll(child, token);
				throw;
			}
			Log.Debug("{0}.Process<{1}> Run Result", current, typeof(TGameState));
			await HandleExitAll(child, token);
		}

		async Task HandleExitAll(StackData data, CancellationToken token)
		{
			try
			{
				await ExitAll(data);
			}
			catch (Exception e)
			{
				if (token.IsCancellationRequested)
				{
					Log.Debug("Cancel");
					return;
				}

				var handle = ErrorHandler;
				if (handle != null)
				{
					handle.Handle(e);
				}
				else
				{
					Debug.LogException(e);
				}
				m_AsyncLock?.SetError(new(e));
			}
		}

		async Task<(StackData, TGameState)> ProcessPush<TGameState>(GameState current, ParameterHolder parameter, CancellationToken token) where TGameState : GameState
		{
			var data = FindStackData(current);
			if (data == null)
			{
				throw new InvalidOperationException("not current state");
			}
			var state = CreateState<TGameState>(parameter);
			StackData child;
			try
			{
				await Decorators.DoPreInitialize(state);
				await state.DoInitialize();
				await Decorators.DoPostInitialize(state);
				child = new StackData();
				child.State = state;
				child.Parent = data;
				data.Children.Add(child);
				await state.DoPreEnter();
				await Decorators.DoPreEnter(state);
				state.DoEnter();
				Decorators.DoPostEnter(state);
			}
			catch (Exception e)
			{
				await Decorators.OnError(state, e);
				var handle = ErrorHandler;
				if (handle != null)
				{
					handle.Handle(e);
				}
				else
				{
					Debug.LogException(e);
				}
				m_AsyncLock?.SetError(new(e));
				throw;
			}
			return (child, state);
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

		internal T FindParent<T>(GameState state) where T : class
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

		internal IEnumerable<T> FindParents<T>(GameState state) where T : class
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

		public IEnumerable<T> FindAllStates<T>() where T : class
		{
			foreach (var state in GetAllStates())
			{
				if (state is T ret)
				{
					yield return ret;
				}
			}
		}

		internal IEnumerable<GameState> GetAllStates()
		{
			return GetImpl(m_Data);

			IEnumerable<GameState> GetImpl(StackData data)
			{
				if (data == null)
				{
					yield break;
				}
				foreach (var child in data.Children)
				{
					if (child.State == null || !child.State.Active)
					{
						continue;
					}
					foreach (var state in GetImpl(child))
					{
						yield return state;
					}
				}
				if (data.State != null && data.State.Active)
				{
					yield return data.State;
				}
			}
		}


	}

}