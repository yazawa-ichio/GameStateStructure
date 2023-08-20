using GameStateStructure.Logger;
using System;
using System.Collections.Generic;
using System.Linq;
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
		void Handle(Exception e);
	}

	public class Config
	{
		public IActivator Activator { get; set; }

		public IErrorHandler ErrorHandler { get; set; }

	}

	public class PrepareCancelException : Exception
	{
		public PrepareCancelException() { }
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
				return Activator.CreateInstance<T>();
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
			await state.DoExit();
		}

		public Task Entry<T>() where T : GameState
		{
			return Entry<T>(null, null);
		}

		public async Task Entry<T>(Config config, Action<T> onCreate) where T : GameState
		{
			await ExitAll(m_Data, remove: true);
			m_Config = config ?? new();
			var data = new StackData();
			var state = CreateState<T>(new ParameterHolder());
			data.State = state;
			onCreate?.Invoke(state);
			m_Data = new StackData();
			m_Data.State = state;
			await state.DoPrepare();
			state.DoEnter();
		}

		async void Handle(Func<Task> task)
		{
			try
			{
				await task();
			}
			catch (PrepareCancelException)
			{
				return;
			}
			catch (OperationCanceledException)
			{
				return;
			}
			catch (Exception e)
			{
				var handle = m_Config?.ErrorHandler;
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

		public void Change<T>(GameState current, ParameterHolder parameter) where T : GameState, new()
		{
			var data = FindStackData(current);
			if (data == null)
			{
				return;
			}
			Handle(async () =>
			{
				var state = CreateState<T>(parameter);
				await state.DoPrepare();
				await ExitAll(data, remove: false);
				data.State = state;
				await current.DoExit();
				state.DoEnter();
			});
		}

		public void Push<T>(GameState current, ParameterHolder parameter, CancellationToken token) where T : GameState, new()
		{
			Log.Debug("{0}.Push<{1}>", current, typeof(T));
			var data = FindStackData(current);
			if (data == null)
			{
				return;
			}
			Handle(async () =>
			{
				var state = CreateState<T>(parameter);
				await state.DoPrepare();
				var child = new StackData();
				child.State = state;
				child.Parent = data;
				data.Children.Add(child);
				state.DoEnter();
			});
		}

		public void Pop(GameState current)
		{
			var data = FindStackData(current);
			if (data == null)
			{
				return;
			}
			Handle(async () =>
			{
				await ExitAll(data, true);
			});
		}

		public async Task<TResult> Module<TGameState, TResult>(GameState current, ParameterHolder parameter, CancellationToken token) where TGameState : GameState, IModule<TResult>
		{
			var data = FindStackData(current);
			if (data == null)
			{
				throw new InvalidOperationException("not current state");
			}
			var state = CreateState<TGameState>(parameter);
			await state.DoPrepare();
			var child = new StackData();
			child.State = state;
			child.Parent = data;
			data.Children.Add(child);
			var ret = await state.Run();
			await ExitAll(child, true);
			return ret;
		}

		public async Task Module<TGameState>(GameState current, ParameterHolder parameter, CancellationToken token) where TGameState : GameState, IModule
		{
			var data = FindStackData(current);
			if (data == null)
			{
				throw new InvalidOperationException("not current state");
			}
			var state = CreateState<TGameState>(parameter);
			await state.DoPrepare();
			var child = new StackData();
			child.State = state;
			child.Parent = data;
			data.Children.Add(child);
			await state.Run();
			await ExitAll(child, true);
		}

		void Update()
		{
			m_Data?.Update();
		}

		StackData FindStackData(GameState state)
		{
			return GetStackDatas().FirstOrDefault(x => x.State == state);
		}

		IEnumerable<StackData> GetStackDatas()
		{
			return GetImpl(m_Data);

			IEnumerable<StackData> GetImpl(StackData data)
			{
				if (data == null)
				{
					yield break;
				}
				yield return data;
				foreach (var child in data.Children)
				{
					foreach (var childData in GetImpl(child))
					{
						yield return childData;
					}
				}
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
				if (data.State != null)
				{
					yield return data.State;
				}
				foreach (var child in data.Children)
				{
					foreach (var state in GetImpl(child))
					{
						yield return state;
					}
				}
			}
		}


	}

}