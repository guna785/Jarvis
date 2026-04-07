using System;
using System.Collections.Generic;
using System.Text;

namespace Jarvis.Contract
{
    public interface IContinuousMicService
    {
        void StartListening();
        void StopListening();
    }
}
