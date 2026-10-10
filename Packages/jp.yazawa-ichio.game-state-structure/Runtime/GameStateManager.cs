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
		public IStateFactory Factory { get; set; }

		public IErrorHandler ErrorHandler { get; set; }

		public DecoratorCollection Decorator { get; private set; } = new DecoratorCollection();
	}

	public partial class GameStateManager : MonoBehaviour
	{

		StackData m_Data;
		AsyncLock m_AsyncLock = new();
		bool m_Destroyed;

		List<StackData> m_Exiting = new List<StackData>();

		int m_Generation;

		readonly struct TransitionScope
		{
			readonly GameStateManager m_Manager;
			readonly int m_Generation;

			/// <summary>開始時のロック。例外で止めるのは常にこちら。</summary>
			public AsyncLock Lock { get; }

			public TransitionScope(GameStateManager manager)
			{
				m_Manager = manager;
				m_Generation = manager.m_Generation;
				Lock = manager.m_AsyncLock;
			}

			/// <summary>Entry で作り直された後か。</summary>
			public bool IsStale => m_Manager == null || m_Generation != m_Manager.m_Generation;

			public void ThrowIfAborted()
			{
				if (IsStale)
				{
					throw new TransitionAbortedException();
				}
			}
		}

		public IStateFactory Factory { get; set; }

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

		T CreateState<T>(ParameterHolder parameter, StackData parent, Type branch, out IDisposable factoryValue)
			where T : GameState
		{
			var (state, value) = New();
			factoryValue = value;
			var context = new Context(state);
			context.SetOwned(value);
			try
			{
				var obj = new GameObject(typeof(T).Name);
				obj.transform.SetParent(transform);
				context.ContextObject = context.Manage(obj);
				state.Setup(this, context);
				parameter.Apply(state);
			}
			catch
			{
				SafeRelease(context);
				throw;
			}
			return state;

			(T, IDisposable) New()
			{
				var factory = Factory;
				if (factory == null)
				{
					return (System.Activator.CreateInstance<T>(), null);
				}
				return factory.Create<T>(new StateCreationContext(branch), parent?.FactoryValue);
			}
		}

		Task ExitAll(StackData data, PopReason reason)
		{
			return ExitAllCore(data, data?.Parent?.State, reason);
		}

		async Task ExitAllCore(StackData data, GameState parentState, PopReason reason)
		{
			// State が null なのは解体済みの印。親の遷移で先に畳まれた子に対して
			// RunProcess の後片付けが再度ここへ来る
			if (data == null || data.State == null)
			{
				return;
			}
			var scope = new TransitionScope(this);
			data.PreCancel();
			data.Parent?.Children.Remove(data);
			var state = data.State;
			data.State = null;
			data.Exiting = state;
			m_Exiting.Add(data);
			var completed = false;
			try
			{
				await Decorators.DoPreExit(state);
				while (data.Children.Count > 0)
				{
					await ExitAllCore(data.Children[0], state, PopReason.ParentExit);
				}
				await state.DoExit();
				if (!scope.IsStale)
				{
					await Decorators.DoPostExit(state);
				}
				completed = true;
			}
			finally
			{
				m_Exiting.Remove(data);
				if (!completed)
				{
					ReleaseExiting(data);
				}
			}
			if (scope.IsStale)
			{
				Log.Debug("DoPop aborted {0}", state);
				return;
			}
			Log.Debug("DoPop {0} ({1})", state, reason);
			Notify(new StateEvent(StateEventType.Exit, state, reason));
			parentState?.OnPopChild(state, reason);
		}

		void ForceRelease(StackData data)
		{
			if (data == null)
			{
				return;
			}
			data.PreCancel();
			foreach (var child in data.Children.ToArray())
			{
				ForceRelease(child);
			}
			data.Children.Clear();
			var released = data.State;
			data.State = null;
			SafeRelease(released?.Context);
			SafeRelease(released?.DescendantDecorators);
			if (released != null)
			{
				Notify(new StateEvent(StateEventType.ForceRelease, released));
			}
		}

		void ForceReleaseExiting()
		{
			var exiting = m_Exiting.ToArray();
			m_Exiting.Clear();
			foreach (var data in exiting)
			{
				var released = data.Exiting;
				ReleaseExiting(data);
				if (released != null)
				{
					Notify(new StateEvent(StateEventType.ForceRelease, released));
				}
			}
		}

		void ReleaseExiting(StackData data)
		{
			foreach (var child in data.Children.ToArray())
			{
				ForceRelease(child);
			}
			data.Children.Clear();
			var released = data.Exiting;
			data.Exiting = null;
			if (released == null)
			{
				return;
			}
			SafeRelease(released.Context);
			SafeRelease(released.DescendantDecorators);
		}


		void SafeRelease(Context context)
		{
			if (context == null)
			{
				return;
			}
			try
			{
				context.ForceDispose();
			}
			catch (Exception e)
			{
				ReportReleaseError(e);
			}
		}

		void SafeRelease(DecoratorCollection decorators)
		{
			if (decorators == null)
			{
				return;
			}
			try
			{
				decorators.ForceRelease();
			}
			catch (Exception e)
			{
				ReportReleaseError(e);
			}
		}

		void ReportReleaseError(Exception e)
		{
			Log.Warning("ForceRelease failed {0}", e);
			ReportError(e);
		}

		// 新しいステートの初期化が終わってから呼ぶこと。失敗したときに既存のステートを畳んでしまわないため
		async Task ExitBranchChild(StackData data, Type branch)
		{
			foreach (var exitChild in data.Children.ToArray())
			{
				if (exitChild.Branch != branch)
				{
					continue;
				}
				await ExitAll(exitChild, PopReason.Replace);
			}
		}

		static void CheckBranchOccupied(StackData data, Type branch)
		{
			if (Branch.Get(branch)?.Occupied != BranchOccupied.Reject)
			{
				return;
			}
			foreach (var child in data.Children)
			{
				if (child.Branch == branch)
				{
					throw new BranchRejectedException(branch);
				}
			}
		}

		async Task<StackData> CreateAndAttach<T>(
			ParameterHolder parameter, StackData parent, Type branch, TransitionScope scope, Func<StackData, Task> attach)
			where T : GameState
		{
			GameState created = null;
			var attached = false;
			try
			{
				var state = CreateState<T>(parameter, parent, branch, out var value);
				created = state;
				await Decorators.DoPreInitialize(state);
				scope.ThrowIfAborted();
				await state.DoInitialize();
				scope.ThrowIfAborted();
				await Decorators.DoPostInitialize(state);
				scope.ThrowIfAborted();

				var data = new StackData();
				data.State = state;
				data.Parent = parent;
				data.Branch = branch;
				data.FactoryValue = value;
				await attach(data);
				attached = true;
				return data;
			}
			finally
			{
				if (!attached)
				{
					SafeRelease(created?.Context);
				}
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
			++m_Generation;
			m_AsyncLock?.SetError(new TransitionHangException());
			try
			{
				ForceRelease(m_Data);
				ForceReleaseExiting();
				SafeRelease(Decorators);
			}
			finally
			{
				m_Data = null;
				m_Exiting.Clear();
				Factory = config.Factory;
				ErrorHandler = config.ErrorHandler;
				Decorators = config.Decorator;
				m_AsyncLock = new AsyncLock();
			}

			var scope = new TransitionScope(this);
			GameState created = null;
			StackData data = null;
			var initialized = false;
			try
			{
				var state = CreateState<T>(parameter, null, null, out var rootValue);
				created = state;
				data = new StackData();
				data.State = state;
				data.FactoryValue = rootValue;
				m_Data = data;
				await Decorators.DoPreInitialize(state);
				scope.ThrowIfAborted();
				await state.DoInitialize();
				scope.ThrowIfAborted();
				await Decorators.DoPostInitialize(state);
				initialized = true;
				await EnterState(state, scope);
			}
			finally
			{
				if (!initialized)
				{
					// 後から来た Entry に差し替えられている場合は触らない
					if (ReferenceEquals(m_Data, data))
					{
						m_Data = null;
					}
					SafeRelease(created?.Context);
				}
			}
		}

		async Task EnterState(GameState state, TransitionScope scope)
		{
			scope.ThrowIfAborted();
			await state.DoPreEnter();
			scope.ThrowIfAborted();
			await Decorators.DoPreEnter(state);
			scope.ThrowIfAborted();
			state.DoEnter();
			Decorators.DoPostEnter(state);
			Notify(new StateEvent(StateEventType.Enter, state));
		}

		void ReportError(Exception e, GameState state = null)
		{
			Notify(new StateEvent(StateEventType.Error, state, default, e));
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

#if GSS_UNITASK && !GSS_DISABLE_UNITASK
		internal void Handle(Func<Cysharp.Threading.Tasks.UniTask> task, bool ignoreCancel = false)
		{
			var scope = new TransitionScope(this);
			Run().Forget();

			async Cysharp.Threading.Tasks.UniTaskVoid Run()
			{
				try
				{
					await task();
				}
				catch (Exception e)
				{
					HandleError(scope, e, ignoreCancel);
				}
			}
		}
#else
		internal async void Handle(Func<Task> task, bool ignoreCancel = false)
		{
			var scope = new TransitionScope(this);
			try
			{
				await task();
			}
			catch (Exception e)
			{
				HandleError(scope, e, ignoreCancel);
			}
		}
#endif

		void HandleError(TransitionScope scope, Exception e, bool ignoreCancel)
		{
			if (e is TransitionAbortedException)
			{
				Log.Debug("TransitionAborted {0}", e);
				return;
			}
			if (scope.IsStale)
			{
				Log.Warning("stale transition failed after Entry {0}", e);
				return;
			}
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
			ReportError(e);
			scope.Lock?.SetError(new(e));
		}

		public void GoTo<T>(GameState current, ParameterHolder parameter) where T : GameState
		{
			var scope = new TransitionScope(this);
			Handle(async () =>
			{
				using var _ = await m_AsyncLock.Enter(m_DefaultCancellationToken);
				FindStackData(current)?.PreCancel();

				var data = FindStackData(current);
				if (data == null)
				{
					return;
				}

				var parent = data.Parent;
				var newData = await CreateAndAttach<T>(parameter, parent, data.Branch, scope, async attaching =>
				{
					await ExitAll(data, PopReason.GoTo);
					scope.ThrowIfAborted();
					if (parent != null)
					{
						parent.Children.Add(attaching);
					}
					else
					{
						m_Data = attaching;
					}
				});
				await EnterState(newData.State, scope);
			});
		}

		public void Push<T>(GameState current, ParameterHolder parameter) where T : GameState
		{
			Log.Debug("{0}.Push<{1}>", current, typeof(T));
			var scope = new TransitionScope(this);
			Handle(async () =>
			{
				using var _ = await m_AsyncLock.Enter(m_DefaultCancellationToken);
				var data = FindStackData(current);
				if (data == null || data.CancellationTokenSource == null)
				{
					return;
				}
				var child = await CreateAndAttach<T>(parameter, data, null, scope, async attaching =>
				{
					await ExitBranchChild(data, null);
					scope.ThrowIfAborted();
					data.Children.Add(attaching);
				});
				await EnterState(child.State, scope);
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
				await ExitAll(data, PopReason.Pop);
			});
		}

		public async Task<TResult> RunProcess<TGameState, TResult>(GameState current, ParameterHolder parameter, Type branch, CancellationToken token) where TGameState : GameState, IProcess<TResult>
		{
			var scope = new TransitionScope(this);
			using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(token, m_DefaultCancellationToken);
			token = linkedCts.Token;

			StackData child;
			TGameState state;

			using (await m_AsyncLock.Enter(token))
			{
				(child, state) = await ProcessPush<TGameState>(current, parameter, branch, token, scope);
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
				scope.ThrowIfAborted();
				using (await m_AsyncLock.Enter(m_DefaultCancellationToken))
				{
					await HandleExitAll(child, token, scope);
				}
				throw;
			}
			Log.Debug("{0}.Process<{1}, {2}> Run Result {3}", current, typeof(TGameState), typeof(TResult), result);
			scope.ThrowIfAborted();
			using (await m_AsyncLock.Enter(m_DefaultCancellationToken))
			{
				await HandleExitAll(child, token, scope);
			}
			return result;
		}

		public async Task RunProcess<TGameState>(GameState current, ParameterHolder parameter, Type branch, CancellationToken token) where TGameState : GameState, IProcess
		{
			var scope = new TransitionScope(this);
			using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(token, m_DefaultCancellationToken);
			token = linkedCts.Token;

			StackData child;
			TGameState state;
			using (await m_AsyncLock.Enter(token))
			{
				(child, state) = await ProcessPush<TGameState>(current, parameter, branch, token, scope);
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
				scope.ThrowIfAborted();
				using (await m_AsyncLock.Enter(m_DefaultCancellationToken))
				{
					await HandleExitAll(child, token, scope);
				}
				throw;
			}
			Log.Debug("{0}.Process<{1}> Run Result", current, typeof(TGameState));
			scope.ThrowIfAborted();
			using (await m_AsyncLock.Enter(m_DefaultCancellationToken))
			{
				await HandleExitAll(child, token, scope);
			}
		}

		async Task HandleExitAll(StackData data, CancellationToken token, TransitionScope scope)
		{
			try
			{
				await ExitAll(data, PopReason.ProcessEnd);
			}
			catch (Exception e)
			{
				if (token.IsCancellationRequested)
				{
					Log.Debug("Cancel");
					return;
				}

				if (scope.IsStale)
				{
					Log.Warning("stale exit failed after Entry {0}", e);
					return;
				}
				ReportError(e);
				scope.Lock?.SetError(new(e));
			}
		}

		async Task<(StackData, TGameState)> ProcessPush<TGameState>(GameState current, ParameterHolder parameter, Type branch, CancellationToken token, TransitionScope scope) where TGameState : GameState
		{
			var data = FindStackData(current);
			if (data == null)
			{
				throw new InvalidOperationException("not current state " + current);
			}
			CheckBranchOccupied(data, branch);
			StackData child;
			try
			{
				child = await CreateAndAttach<TGameState>(parameter, data, branch, scope, async attaching =>
				{
					await ExitBranchChild(data, branch);
					scope.ThrowIfAborted();
					data.Children.Add(attaching);
				});
				await EnterState(child.State, scope);
			}
			catch (Exception e)
			{
				if (e is TransitionAbortedException)
				{
					throw;
				}
				if (scope.IsStale)
				{
					throw new TransitionAbortedException();
				}
				ReportError(e);
				scope.Lock?.SetError(new(e));
				throw;
			}
			return (child, (TGameState)child.State);
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

		internal bool IsLeaf(GameState state)
		{
			var data = FindStackData(state);
			if (data == null)
			{
				return false;
			}
			return data.Children.Count == 0;
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