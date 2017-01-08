/*
 * Copyright (C) 2016 Vyacheslav Shevelyov (slavash at aha dot ru)
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with this program.  If not, see http://www.gnu.org/licenses/.
 */

using System;

namespace Epanet.Util {

    ///<summary>Epanet exception codes handler.</summary>
    public class ENException:Exception {

        ///<summary>Array of arguments to be used in the error string creation.</summary>
        private readonly object[] _arguments;

        ///<summary>Epanet error code.</summary>
        private readonly ErrorCode _code;

        /// <summary>Get error code.</summary>
        /// <value>Code id.</value>
        public ErrorCode Code {
            get { return this._code; }
        }

        ///<summary>Contructor from error code id.</summary>
        ///<param name="code">Error code id.</param>

        public ENException(ErrorCode code) {
            this._arguments = null;
            this._code = code;
        }

        /// <summary>Contructor from error code id and multiple arguments.</summary>
        ///  <param name="code">Error code id.</param>
        /// <param name="arg">Extra arguments.</param>
        ///  
        public ENException(ErrorCode code, params object[] arg) {
            this._code = code;
            this._arguments = arg;
        }

        ///<summary>Contructor from other exception and multiple arguments.</summary>
        public ENException(ENException e, params object[] arg) {
            this._arguments = arg;
            this._code = e.Code;
        }

        ///<summary>Get arguments array.</summary>
        public object[] Arguments { get { return this._arguments; } }

        ///<summary>Handles the exception string conversion.</summary>
        /// <returns>Final error string.</returns>
        public override string Message {
            get {
                string str;
                string name = "ERR" + (int)this._code;

                try {
                    str = Properties.Error.ResourceManager.GetString(name);
                }
                catch (Exception) {
                    str = null;
                }


                if (str == null)
                    return string.Format("Unknown error message ({0})", this._code);

                if (this._arguments != null)
                    return string.Format(str, this._arguments);

                return str;
            }
        }
    }

}