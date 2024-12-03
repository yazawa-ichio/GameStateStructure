using System;

namespace GameStateStructure
{
	public interface IErrorHandler
	{
		void Handle(Exception ex);
	}

}