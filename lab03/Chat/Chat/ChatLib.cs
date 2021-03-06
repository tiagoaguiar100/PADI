﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ChatLib
{
    public interface IChatServer
    {
        void send(String nick, String msg);
        void connect(String nick, String url);
    }

    public interface IChatClient
    {
        void broadcast(String nick, String msg);
    }
}
