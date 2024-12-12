using System;
using System.Threading.Tasks;

namespace GameStateStructure
{

	public interface IDecorator : IDisposable
	{
		int Priority { get; }
		Task DoPreInitialize(GameState state);
		Task DoPostInitialize(GameState state);
		Task DoPreEnter(GameState state);
		void DoPostEnter(GameState state);
		Task DoPreExit(GameState state);
		Task DoPostExit(GameState state);
	}

	public abstract class Decorator<TMaker> : IDecorator where TMaker : class
	{
		static bool s_IsAttribute = typeof(Attribute).IsAssignableFrom(typeof(TMaker));

		public virtual int Priority => 0;

		TMaker GetMaker(GameState state)
		{
			if (s_IsAttribute)
			{
				return MakerAttributeCache<TMaker>.Get(state.GetType()).Maker;
			}
			return state as TMaker;
		}

		protected TMaker[] GetMakers(GameState state)
		{
			if (s_IsAttribute)
			{
				return MakerAttributeCache<TMaker>.Get(state.GetType()).Makers;
			}
			throw new InvalidOperationException("TMaker is Attribute only");
		}

		Task IDecorator.DoPreInitialize(GameState state)
		{
			var maker = GetMaker(state);
			if (maker == null)
			{
				return Task.CompletedTask;
			}
			return OnPreInitialize(state, maker);
		}

		protected virtual Task OnPreInitialize(GameState state, TMaker maker)
		{
			return Task.CompletedTask;
		}

		Task IDecorator.DoPostInitialize(GameState state)
		{
			var maker = GetMaker(state);
			if (maker == null)
			{
				return Task.CompletedTask;
			}
			return OnPostInitialize(state, maker);
		}

		protected virtual Task OnPostInitialize(GameState state, TMaker maker)
		{
			return Task.CompletedTask;
		}

		Task IDecorator.DoPreEnter(GameState state)
		{
			var maker = GetMaker(state);
			if (maker == null)
			{
				return Task.CompletedTask;
			}
			return OnPreEnter(state, maker);
		}

		protected virtual Task OnPreEnter(GameState state, TMaker maker)
		{
			return Task.CompletedTask;
		}

		void IDecorator.DoPostEnter(GameState state)
		{
			var maker = GetMaker(state);
			if (maker == null)
			{
				return;
			}
			OnPostEnter(state, maker);
		}

		protected virtual void OnPostEnter(GameState state, TMaker maker)
		{
		}

		Task IDecorator.DoPreExit(GameState state)
		{
			var maker = GetMaker(state);
			if (maker == null)
			{
				return Task.CompletedTask;
			}
			return OnPreExit(state, maker);
		}

		protected virtual Task OnPreExit(GameState state, TMaker maker)
		{
			return Task.CompletedTask;
		}

		Task IDecorator.DoPostExit(GameState state)
		{
			var maker = GetMaker(state);
			if (maker == null)
			{
				return Task.CompletedTask;
			}
			return OnPostExit(state, maker);
		}

		protected virtual Task OnPostExit(GameState state, TMaker maker)
		{
			return Task.CompletedTask;
		}

		protected virtual Task OnModulRun(GameState state, TMaker maker)
		{
			return Task.CompletedTask;
		}

		void IDisposable.Dispose() { }

		protected virtual void Dispose()
		{
		}
	}

}