using System;
using System.Collections.Generic;
using System.Text;

namespace Visor.Contract
{
    public interface IContinuousMicService
    {
        void StartListening();
        void StopListening();
    }
}
