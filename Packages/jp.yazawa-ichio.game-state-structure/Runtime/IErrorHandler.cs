using System;

namespace GameStateStructure
{
	public interface IErrorHandler
	{
		void Handle(Exception ex);
	}

	public class ErrorHandler : IErrorHandler
	{
		public static ErrorHandler Create(Action<Exception> handler) => new ErrorHandler(handler);

		Action<Exception> m_Handler;

		public ErrorHandler(Action<Exception> handler)
		{
			m_Handler = handler;
		}

		public void Handle(Exception ex)
		{
			m_Handler?.Invoke(ex);
		}

	}

}