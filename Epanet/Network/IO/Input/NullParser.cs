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

using System.Diagnostics;

namespace Epanet.Network.IO.Input {

    ///<summary>Network conversion units only class.</summary>
    public class NullParser:InputParser {
        public NullParser(TraceSource log):base(log) { }

        public override Network Parse(Network net, string f) {
            AdjustData(net);
            net.FieldsMap.Prepare(
                   net.PropertiesMap.Unitsflag,
                   net.PropertiesMap.Flowflag,
                   net.PropertiesMap.Pressflag,
                   net.PropertiesMap.Qualflag,
                   net.PropertiesMap.ChemUnits,
                   net.PropertiesMap.SpGrav,
                   net.PropertiesMap.Hstep);
            this.Convert(net);
            return net;
        }
    }

}