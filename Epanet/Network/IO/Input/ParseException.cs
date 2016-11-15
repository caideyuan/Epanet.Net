﻿using Epanet.Util;

namespace Epanet.Network.IO.Input {
    public sealed class EpanetParseException : ENException {

        public EpanetParseException(ErrorCode id, int line, string file, params object[] args) : base(id, args) {
            this.File = file;
            this.Line = line;
        }

        public string File { get; private set; }
        public int Line { get; private set; }

        public override string Message { get { return this.File + "(" + this.Line + "):" + base.Message; } }


    }
}