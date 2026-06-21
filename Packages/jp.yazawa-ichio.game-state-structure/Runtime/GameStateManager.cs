using GameStateStructure.Logger;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Pool;

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

		StackData m_Data;
		AsyncLock m_AsyncLock = new();
		bool m_Destroyed;

		public IActivator Activator { get; set; }

		public IErrorHandler ErrorHandler { get; set; }

		public DecoratorCollection Decorators { get; private set; }

		public bool IsBusy => m_AsyncLock?.IsLocked ?? false;

		public GameState Root => m_Data?.State;

		CancellationToken m_DefaultCancellationToken;

		private void Awake()
		{
			m_DefaultCancellationToken = destroyCancellationToken;
			Log.Debug("Awake {0}", this);
			All.Add(this);
		}

		private void OnDestroy()
		{
			Log.Debug("OnDestroy {0}", this);
			All.Remove(this);
			m_Destroyed = true;
			m_AsyncLock?.SetError(new TransitionHangException(new ObjectDisposedException("object destroyed")));
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
			context.ContextObject = context.Manage(obj);
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
			data.Parent?.Children.Remove(data);
			var state = data.State;
			data.State = null;
			await Decorators.DoPreExit(state);
			var children = data.Children.ToArray();
			data.Children.Clear();
			foreach (var child in children)
			{
				await ExitAll(child);
			}
			await state.DoExit();
			await Decorators.DoPostExit(state);
			Log.Debug("DoPop {0}", state);
			data.Parent?.State?.OnPopChild(state);
		}

		async Task ForceRelease(StackData data)
		{
			if (data == null)
			{
				return;
			}
			data.PreCancel();
			foreach (var child in data.Children.ToArray())
			{
				await ForceRelease(child);
			}
			var dispose = data.State?.Context?.DisposeAsync();
			if (dispose != null)
			{
				await dispose;
			}
			var destroy = data.State?.Decorators?.ForceRelease();
			if (destroy != null)
			{
				await destroy;
			}
		}

		public Task Entry<T>() where T : GameState
		{
			return Entry<T>(new(), new());
		}

		public Task Entry<T>(Config config) where T : GameState
		{
			return Entry<T>(new(), config);
		}

		public async Task Entry<T>(ParameterHolder parameter, Config config) where T : GameState
		{
			m_AsyncLock?.SetError(new TransitionHangException());
			await ForceRelease(m_Data);
			if (Decorators != null)
			{
				await Decorators.ForceRelease();
			}
			Activator = config.Activator;
			ErrorHandler = config.ErrorHandler;
			Decorators = config.Decorator;
			m_AsyncLock = new AsyncLock();

			var data = new StackData();
			var state = CreateState<T>(parameter);
			data.State = state;
			m_Data = new StackData();
			m_Data.State = state;
			await Decorators.DoPreInitialize(state);
			await state.DoInitialize();
			await Decorators.DoPostInitialize(state);
			await state.DoPreEnter();
			await Decorators.DoPreEnter(state);
			state.DoEnter();
			Decorators.DoPostEnter(state);
		}

		internal async void Handle(Func<Task> task, bool ignoreCancel = false)
		{
			try
			{
				await task();
			}
			catch (Exception e)
			{
				if (ignoreCancel && e is OperationCanceledException)
				{
					Log.Debug("OperationCanceledException {0}", e);
					return;
				}
				if (m_Destroyed)
				{
					Log.Warning("Object Destroyed {0}", e);
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

		public void GoTo<T>(GameState current, ParameterHolder parameter) where T : GameState, new()
		{
			Handle(async () =>
			{
				using var _ = await m_AsyncLock.Enter(m_DefaultCancellationToken);
				FindStackData(current)?.PreCancel();

				var data = FindStackData(current);
				if (data == null)
				{
					return;
				}

				var state = CreateState<T>(parameter);
				await Decorators.DoPreInitialize(state);
				await state.DoInitialize();
				await Decorators.DoPostInitialize(state);
				var parent = data.Parent;
				await ExitAll(data);
				var newData = new StackData();
				newData.State = state;
				newData.Parent = parent;
				if (parent != null)
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
			});
		}

		public void Push<T>(GameState current, ParameterHolder parameter) where T : GameState, new()
		{
			Log.Debug("{0}.Push<{1}>", current, typeof(T));
			Handle(async () =>
			{
				using var _ = await m_AsyncLock.Enter(m_DefaultCancellationToken);
				var data = FindStackData(current);
				if (data == null || data.CancellationTokenSource == null)
				{
					return;
				}
				var state = CreateState<T>(parameter);
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
			});
		}

		public void Pop(GameState current)
		{
			Handle(async () =>
			{
				using var _ = await m_AsyncLock.Enter(m_DefaultCancellationToken);
				FindStackData(current)?.PreCancel();
				if (current.IsRoot)
				{
					throw new InvalidOperationException("Root state can not be popped");
				}
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
			using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(token, m_DefaultCancellationToken);
			token = linkedCts.Token;

			StackData child;
			TGameState state;

			using (await m_AsyncLock.Enter(token))
			{
				(child, state) = await ProcessPush<TGameState>(current, parameter, token);
			}

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
				using (await m_AsyncLock.Enter(m_DefaultCancellationToken))
				{
					await HandleExitAll(child, token);
				}
				throw;
			}
			Log.Debug("{0}.Process<{1}, {2}> Run Result {3}", current, typeof(TGameState), typeof(TResult), result);
			using (await m_AsyncLock.Enter(m_DefaultCancellationToken))
			{
				await HandleExitAll(child, token);
			}
			return result;
		}

		public async Task RunProcess<TGameState>(GameState current, ParameterHolder parameter, CancellationToken token) where TGameState : GameState, IProcess
		{
			using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(token, m_DefaultCancellationToken);
			token = linkedCts.Token;

			StackData child;
			TGameState state;
			using (await m_AsyncLock.Enter(token))
			{
				(child, state) = await ProcessPush<TGameState>(current, parameter, token);
			}

			using var cts = CancellationTokenSource.CreateLinkedTokenSource(token, child.CancellationTokenSource.Token);
			token = cts.Token;

			Log.Debug("{0}.Process<{1}> Run Start", current, typeof(TGameState));
			try
			{
				await state.Run(token);
			}
			catch
			{
				using (await m_AsyncLock.Enter(m_DefaultCancellationToken))
				{
					await HandleExitAll(child, token);
				}
				throw;
			}
			Log.Debug("{0}.Process<{1}> Run Result", current, typeof(TGameState));
			using (await m_AsyncLock.Enter(m_DefaultCancellationToken))
			{
				await HandleExitAll(child, token);
			}
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
				throw new InvalidOperationException("not current state " + current);
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
			using var _ = ListPool<T>.Get(out var list);
			FindAllStates(list);
			foreach (var state in list)
			{
				yield return state;
			}
		}

		public void FindAllStates<T>(List<T> list) where T : class
		{
			using var _ = ListPool<GameState>.Get(out var tmp);
			GetAllStates(tmp);
			foreach (var state in tmp)
			{
				if (state is T ret)
				{
					list.Add(ret);
				}
			}
		}

		public T FindState<T>() where T : class
		{
			using var _ = ListPool<GameState>.Get(out var tmp);
			GetAllStates(tmp);
			foreach (var state in tmp)
			{
				if (state is T ret)
				{
					return ret;
				}
			}
			return default;
		}

		void GetAllStates(List<GameState> list)
		{
			GetImpl(m_Data, list);

			void GetImpl(StackData data, List<GameState> list)
			{
				if (data == null)
				{
					return;
				}
				foreach (var child in data.Children)
				{
					if (child.State == null || !child.State.Active)
					{
						continue;
					}
					GetImpl(child, list);
				}
				if (data.State != null && data.State.Active)
				{
					list.Add(data.State);
				}
			}
		}


	}

}