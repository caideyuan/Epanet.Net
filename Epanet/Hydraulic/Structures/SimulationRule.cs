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
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

using Epanet.Enums;
using Epanet.Log;
using Epanet.Network;
using Epanet.Network.Structures;
using Epanet.Util;

using EpanetNetwork = Epanet.Network.Network;

namespace Epanet.Hydraulic.Structures {

    public class SimulationRule {
        /// <summary>Temporary action item</summary>
        private struct ActItem {
            public ActItem(SimulationRule r, Action a) {
                rule = r;
                action = a;
            }

            public readonly SimulationRule rule;
            public readonly Action action;
        }

        /// <summary>Rule premise.</summary>
        private class Premise {
            public Premise(
                string[] tok,
                Rulewords lOp,
                IEnumerable<SimulationNode> nodes,
                IEnumerable<SimulationLink> links) {
                Varwords lVar;
                object lObj;
                Operators lROp;

                if (tok.Length != 5 && tok.Length != 6)
                    throw new EnException(ErrorCode.Err201);

                tok[1].TryParse(out Objects loType);

                if (loType == Objects.SYSTEM) {
                    EnumsTxt.TryParse(tok[2], out lVar);

                    switch (lVar) {
                        case Varwords.DEMAND:
                        case Varwords.TIME:
                        case Varwords.CLOCKTIME:
                            lObj = Objects.SYSTEM;
                            break;
                        default:
                            throw new EnException(ErrorCode.Err201);
                    }
                }
                else {
                    if (!EnumsTxt.TryParse(tok[3], out lVar))
                        throw new EnException(ErrorCode.Err201);

                    switch (loType) {
                        case Objects.NODE:
                        case Objects.JUNC:
                        case Objects.RESERV:
                        case Objects.TANK:
                            loType = Objects.NODE;
                            break;
                        case Objects.LINK:
                        case Objects.PIPE:
                        case Objects.PUMP:
                        case Objects.VALVE:
                            loType = Objects.LINK;
                            break;
                        default:
                            throw new EnException(ErrorCode.Err201);
                    }

                    if (loType == Objects.NODE) {
                        SimulationNode node = nodes.FirstOrDefault(
                            simNode => simNode.Node.Name.Equals(tok[2], StringComparison.OrdinalIgnoreCase));

                        if (node == null)
                            throw new EnException(ErrorCode.Err203);

                        switch (lVar) {
                            case Varwords.DEMAND:
                            case Varwords.HEAD:
                            case Varwords.GRADE:
                            case Varwords.LEVEL:
                            case Varwords.PRESSURE:
                                break;
                            case Varwords.FILLTIME:
                            case Varwords.DRAINTIME:
                                if (node is SimulationTank)
                                    throw new EnException(ErrorCode.Err201);

                                break;

                            default:
                                throw new EnException(ErrorCode.Err201);
                        }

                        lObj = node;
                    }
                    else {
                        SimulationLink link = links
                            .FirstOrDefault(
                                simLink => simLink.Link.Name.Equals(tok[2], StringComparison.OrdinalIgnoreCase));

                        if (link == null)
                            throw new EnException(ErrorCode.Err204);

                        switch (lVar) {
                            case Varwords.FLOW:
                            case Varwords.STATUS:
                            case Varwords.SETTING:
                                break;
                            default:
                                throw new EnException(ErrorCode.Err201);
                        }

                        lObj = link;
                    }
                }

                if (!(loType == Objects.SYSTEM ? tok[3] : tok[4]).TryParse(out Operators op))
                    throw new EnException(ErrorCode.Err201);

                switch (op) {
                    case Operators.IS:
                        lROp = Operators.EQ;
                        break;
                    case Operators.NOT:
                        lROp = Operators.NE;
                        break;
                    case Operators.BELOW:
                        lROp = Operators.LT;
                        break;
                    case Operators.ABOVE:
                        lROp = Operators.GT;
                        break;
                    default:
                        lROp = op;
                        break;
                }

                // BUG: Baseform bug lStat == Rule.Values.IS_NUMBER
                Values lStat = Values.IS_NUMBER;
                double lVal = double.NaN;

                if (lVar == Varwords.TIME || lVar == Varwords.CLOCKTIME) {
                    lVal = tok.Length == 6
                        ? Utilities.GetHour(tok[4], tok[5])
                        : Utilities.GetHour(tok[4]);

                    lVal *= 3600;

                    if (lVal < 0.0)
                        throw new EnException(ErrorCode.Err202);
                }
                else {
                    if (!EnumsTxt.TryParse(tok[tok.Length - 1], out Values k) || lStat <= Values.IS_NUMBER) {
                        if (lStat == (Values)(-1) || lStat <= Values.IS_NUMBER) {
                            if (!tok[tok.Length - 1].ToDouble(out lVal))
                                throw new EnException(ErrorCode.Err202);

                            if (lVar == Varwords.FILLTIME || lVar == Varwords.DRAINTIME)
                                lVal *= 3600.0;
                        }
                    }
                    else {
                        lStat = k;
                    }

                }

                _status = lStat;
                _value = lVal;
                logop = lOp;
                _relop = lROp;
                _variable = lVar;
                _object = lObj;
            }

            /// <summary>Logical operator</summary>
            public readonly Rulewords logop;
            private readonly object _object;
            /// <summary>Pressure, flow, etc</summary>
            private readonly Varwords _variable;
            /// <summary>Relational operator</summary>
            private readonly Operators _relop;
            /// <summary>Variable's status</summary>
            private readonly Values _status;
            /// <summary>Variable's value</summary>
            private readonly double _value;

            /// <summary>Checks if a particular premise is true.</summary>
            public bool CheckPremise(
                EpanetNetwork net,
                TimeSpan time1,
                TimeSpan htime,
                double dsystem) {

                if (_variable == Varwords.TIME || _variable == Varwords.CLOCKTIME)
                    return CheckTime(net, time1, htime);

                return _status > Values.IS_NUMBER
                    ? CheckStatus()
                    : CheckValue(net.FieldsMap, dsystem);
            }

            /// <summary>Checks if condition on system time holds.</summary>
            private bool CheckTime(EpanetNetwork net, TimeSpan time1, TimeSpan htime) {
                TimeSpan t1, t2;

                switch (_variable) {
                    case Varwords.TIME:
                        t1 = time1;
                        t2 = htime;
                        break;
                    case Varwords.CLOCKTIME:
                        t1 = (time1 + net.Tstart).TimeOfDay();
                        t2 = (htime + net.Tstart).TimeOfDay();
                        break;
                    default:
                        return false;
                }

                var x = TimeSpan.FromSeconds(_value);

                switch (_relop) {
                    case Operators.LT:
                        if (t2 >= x) return false;

                        break;
                    case Operators.LE:
                        if (t2 > x) return false;

                        break;
                    case Operators.GT:
                        if (t2 <= x) return false;

                        break;
                    case Operators.GE:
                        if (t2 < x) return false;

                        break;

                    case Operators.EQ:
                    case Operators.NE:
                        var flag = false;
                        if (t2 < t1) {
                            if (x >= t1 || x <= t2) flag = true;
                        }
                        else {
                            if (x >= t1 && x <= t2) flag = true;
                        }

                        switch (_relop) {
                            case Operators.EQ:
                                if (!flag) return true;

                                break;
                            case Operators.NE: {
                                if (flag) return true;

                                break;
                            }
                        }

                        break;
                }

                return true;
            }

            /// <summary>Checks if condition on link status holds.</summary>
            private bool CheckStatus() {
                switch (_status) {
                    case Values.IS_OPEN:
                    case Values.IS_CLOSED:
                    case Values.IS_ACTIVE:
                        Values j;
                        var simlink = _object as SimulationLink;
                        StatType i = simlink?.SimStatus ?? (StatType)(-1);

                        if (i >= StatType.XHEAD && i <= StatType.CLOSED)
                            j = Values.IS_CLOSED;
                        else if (i == StatType.ACTIVE)
                            j = Values.IS_ACTIVE;
                        else
                            j = Values.IS_OPEN;

                        if (j == _status && _relop == Operators.EQ)
                            return true;
                        if (j != _status && _relop == Operators.NE)
                            return true;

                        break;
                }

                return false;
            }

            /// <summary>Checks if numerical condition on a variable is true.</summary>
            private bool CheckValue(FieldsMap fMap, double dsystem) {
                const double TOL = 0.001D;
                double x;

                switch (_variable) {
                    case Varwords.DEMAND: {
                        switch (_object) {
                            case Objects o when o == Objects.SYSTEM:
                                x = dsystem * fMap.GetUnits(FieldType.DEMAND);
                                break;
                            case SimulationNode node:
                                x = node.SimDemand * fMap.GetUnits(FieldType.DEMAND);
                                break;
                            default:
                                return false;
                        }
                    }
                        break;

                    case Varwords.HEAD:
                    case Varwords.GRADE: {
                        if (!(_object is SimulationNode node))
                            return false;

                        x = node.SimHead * fMap.GetUnits(FieldType.HEAD);
                    }
                        break;

                    case Varwords.PRESSURE: {
                        if (!(_object is SimulationNode node))
                            return false;

                        x = (node.SimHead - node.Elevation) * fMap.GetUnits(FieldType.PRESSURE);
                    }
                        break;

                    case Varwords.LEVEL: {
                        if (!(_object is SimulationNode node))
                            return false;

                        x = (node.SimHead - node.Elevation) * fMap.GetUnits(FieldType.HEAD);
                    }
                        break;

                    case Varwords.FLOW: {
                        if (!(_object is SimulationLink link))
                            return false;

                        x = Math.Abs(link.SimFlow) * fMap.GetUnits(FieldType.FLOW);
                    }
                        break;

                    case Varwords.SETTING: {
                        if (!(_object is SimulationLink link) || double.IsNaN(link.SimSetting))
                            return false;

                        x = link.SimSetting;

                        if (link.LinkType == LinkType.VALVE) {
                            switch (((SimulationValve)link).ValveType) {
                                case ValveType.PRV:
                                case ValveType.PSV:
                                case ValveType.PBV:
                                    x *= fMap.GetUnits(FieldType.PRESSURE);
                                    break;
                                case ValveType.FCV:
                                    x *= fMap.GetUnits(FieldType.FLOW);
                                    break;
                            }
                        }

                        break;
                    }

                    case Varwords.FILLTIME: {
                        if (!(_object is SimulationTank tank) || 
                            tank.IsReservoir || 
                            tank.SimDemand <= Constants.TINY)
                            return false;

                        x = (tank.Vmax - tank.SimVolume) / tank.SimDemand;

                        break;
                    }

                    case Varwords.DRAINTIME: {
                        if (!(_object is SimulationTank tank) || 
                            tank.IsReservoir || 
                            tank.SimDemand >= -Constants.TINY)
                            return false;

                        x = (tank.Vmin - tank.SimVolume) / tank.SimDemand;
                        break;
                    }

                    default:
                        return false;
                }

                switch (_relop) {
                    case Operators.EQ:
                        if (Math.Abs(x - _value) > TOL) return false;
                        break;
                    case Operators.NE:
                        if (Math.Abs(x - _value) < TOL) return false;
                        break;
                    case Operators.LT:
                        if (x > _value + TOL) return false;
                        break;
                    case Operators.LE:
                        if (x > _value - TOL) return false;
                        break;
                    case Operators.GT:
                        if (x < _value - TOL) return false;
                        break;
                    case Operators.GE:
                        if (x < _value + TOL) return false;
                        break;
                }

                return true;
            }

        }

        private class Action {
            private readonly string _label;

            public Action(string[] tok, IEnumerable<SimulationLink> links, string label) {
                _label = label;

                int ntokens = tok.Length;

                if (ntokens != 6)
                    throw new EnException(ErrorCode.Err201);

                SimulationLink slink = links.FirstOrDefault(l => l.Link.Name.Equals(tok[2], StringComparison.OrdinalIgnoreCase));

                if (slink == null)
                    throw new EnException(ErrorCode.Err204);

                if (slink.LinkType == LinkType.PIPE && ((Pipe)slink.Link).HasCheckValve)
                    throw new EnException(ErrorCode.Err207);

                var s = (Values)(-1);
                double x = double.NaN;

                if (EnumsTxt.TryParse(tok[5], out Values k) && k > Values.IS_NUMBER) {
                    s = k;
                }
                else {
                    if (!tok[5].ToDouble(out x) || x < 0.0)
                        throw new EnException(ErrorCode.Err202);
                }

                if (!double.IsNaN(x)) {
                    if (slink.LinkType == LinkType.VALVE)
                        if (((SimulationValve)slink).ValveType == ValveType.GPV)
                            throw new EnException(ErrorCode.Err202);
                }

                if (!double.IsNaN(x) && slink.LinkType == LinkType.PIPE) {
                    s = x.IsZero() ? Values.IS_CLOSED : Values.IS_OPEN;
                    x = double.NaN;
                }

                link = slink;
                _status = s;
                _setting = x;
            }

            internal readonly SimulationLink link; // FIXME internal?
            private readonly Values _status;
            private readonly double _setting;

            /// <summary>Execute action, returns true if the link was alterated.</summary>
            public bool Execute(EpanetNetwork net, TraceSource log, double tol, TimeSpan htime) {
                bool flag = false;

                StatType s = link.SimStatus;
                double v = link.SimSetting;
                double x = _setting;

                if (_status == Values.IS_OPEN && s <= StatType.CLOSED) {
                    // Switch link from closed to open
                    link.SetLinkStatus(true);
                    flag = true;
                }
                else if (_status == Values.IS_CLOSED && s > StatType.CLOSED) {
                    // Switch link from not closed to closed
                    link.SetLinkStatus(false);
                    flag = true;
                }
                else if (!double.IsNaN(x)) {
                    // Change link's setting
                    if (link.LinkType == LinkType.VALVE) {
                        switch (((Valve)link.Link).ValveType) {
                            case ValveType.PRV:
                            case ValveType.PSV:
                            case ValveType.PBV:
                                x /= net.FieldsMap.GetUnits(FieldType.PRESSURE);
                                break;
                            case ValveType.FCV:
                                x /= net.FieldsMap.GetUnits(FieldType.FLOW);
                                break;
                        }
                    }

                    if (Math.Abs(x - v) > tol) {
                        link.SetLinkSetting(x);
                        flag = true;
                    }
                }

                if (flag) {
                    if (net.StatFlag > 0) // Report rule action
                        LogRuleExecution(log, htime);
                    return true;
                }

                return false;
            }

            private void LogRuleExecution(TraceSource log, TimeSpan htime) {
                log.Warning(
                    Properties.Text.FMT63,
                    htime.GetClockTime(),
                    link.LinkType.Keyword2(),
                    link.Link.Name,
                    _label);
            }
        }


        private readonly string _label;
        private readonly double _priority;
        private readonly List<Premise> _pchain = new List<Premise>();
        private readonly List<Action> _tchain = new List<Action>();
        private readonly List<Action> _fchain = new List<Action>();


        // Simulation Methods


        /// <summary>Evaluate rule premises.</summary>
        private bool EvalPremises(
            EpanetNetwork net,
            TimeSpan time1,
            TimeSpan htime,
            double dsystem) {
            bool result = true;

            foreach (var p  in  _pchain) {
                if (p.logop == Rulewords.OR) {
                    if (!result)
                        result = p.CheckPremise(net, time1, htime, dsystem);
                }
                else {
                    if (!result)
                        return false;
                    result = p.CheckPremise(net, time1, htime, dsystem);
                }

            }
            return result;
        }

        /// <summary>Adds rule's actions to action list.</summary>
        private static void UpdateActionList(SimulationRule rule, List<ActItem> actionList, bool branch) {
            if (branch) {
                // go through the true action branch
                foreach (Action a  in  rule._tchain) {
                    if (!CheckAction(rule, a, actionList)) // add a new action from the "true" chain
                        actionList.Add(new ActItem(rule, a));
                }
            }
            else {
                foreach (Action a  in  rule._fchain) {
                    if (!CheckAction(rule, a, actionList)) // add a new action from the "false" chain
                        actionList.Add(new ActItem(rule, a));
                }
            }
        }

        /// <summary>Checks if an action with the same link is already on the Action List.</summary>
        private static bool CheckAction(SimulationRule rule, Action action, List<ActItem> actionList) {
           
            
            for (int i = 0; i < actionList.Count; i++) {

                if(actionList[i].action.link != action.link)
                    continue;

                // Action with same link
                if(rule._priority > actionList[i].rule._priority) {
                    // Replace Actitem action with higher priority rule
                    actionList[i] = new ActItem(rule, action);
                }

                return true;
            }

            return false;
        }

        /// <summary>Implements actions on action list, returns the number of actions executed.</summary>
        private static int TakeActions(EpanetNetwork net, TraceSource log, List<ActItem> actionList, TimeSpan htime) {
            double tol = 1e-3;
            int n = 0;

            foreach (ActItem item  in  actionList) {
                if (item.action.Execute(net, log, tol, htime))
                    n++;
            }

            return n;
        }


        /// <summary>Checks which rules should fire at current time.</summary>
        private static int Check(
            EpanetNetwork net,
            IEnumerable<SimulationRule> rules,
            TraceSource log,
            TimeSpan htime,
            TimeSpan dt,
            double dsystem) {
            // Start of rule evaluation time interval
            TimeSpan time1 = (htime - dt).Add(new TimeSpan(TimeSpan.TicksPerSecond));

            List<ActItem> actionList = new List<ActItem>();

            foreach (SimulationRule rule  in  rules)
                UpdateActionList(rule, actionList, rule.EvalPremises(net, time1, htime, dsystem));

            return TakeActions(net, log, actionList, htime);
        }

        /// <summary>
        /// Updates next time step by checking if any rules will fire before then; 
        /// also updates tank levels.
        /// </summary>
        public static void MinimumTimeStep(
            EpanetNetwork net,
            TraceSource log,
            SimulationRule[] rules,
            List<SimulationTank> tanks,
            TimeSpan htime,
            TimeSpan tstep,
            double dsystem,
            out TimeSpan tstepOut,
            out TimeSpan htimeOut) {

            TimeSpan dt; // Normal time increment for rule evaluation
            TimeSpan dt1; // Actual time increment for rule evaluation

            // Find interval of time for rule evaluation
            TimeSpan tnow = htime;        // Start of time interval for rule evaluation
            TimeSpan tmax = tnow + tstep; // End of time interval for rule evaluation

            //If no rules, then time increment equals current time step
            if (rules.Length == 0) {
                dt = tstep;
                dt1 = dt;
            }
            else {
                // Otherwise, time increment equals rule evaluation time step and
                // first actual increment equals time until next even multiple of
                // Rulestep occurs.
                dt = net.RuleStep;
                dt1 = net.RuleStep - new TimeSpan(tnow.Ticks % net.RuleStep.Ticks);
            }

            // Make sure time increment is no larger than current time step
            dt = new TimeSpan(Math.Min(dt.Ticks, tstep.Ticks));
            dt1 = new TimeSpan(Math.Min(dt1.Ticks, tstep.Ticks));

            if (dt1.IsZero())
                dt1 = dt;

            // Step through time, updating tank levels, until either
            // a rule fires or we reach the end of evaluation period.
            //
            // Note: we are updating the global simulation time (Htime)
            //       here because it is used by functions in RULES.C(this class)
            //       to evaluate rules when checkrules() is called.
            //       It is restored to its original value after the
            //       rule evaluation process is completed (see below).
            //       Also note that dt1 will equal dt after the first
            //       time increment is taken.

            do {
                htime += dt1; // Update simulation clock
                SimulationTank.StepWaterLevels(tanks, net.FieldsMap, dt1); // Find new tank levels
                if (Check(net, rules, log, htime, dt1, dsystem) != 0) break; // Stop if rules fire
                dt = new TimeSpan(Math.Min(dt.Ticks, (tmax - htime).Ticks)); // Update time increment
                dt1 = dt; // Update actual increment
            }
            while (dt > TimeSpan.Zero);

            //Compute an updated simulation time step (*tstep)
            // and return simulation time to its original value
            tstepOut = htime - tnow;
            htimeOut = tnow;

        }

        public SimulationRule(Rule rule, IList<SimulationLink> links, IList<SimulationNode> nodes) {
            _label = rule.Name;

            double tempPriority = 0.0;

            Rulewords ruleState = Rulewords.RULE;

            foreach (string line in rule.Code) {
                string[] tok = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);

                if (!tok[0].TryParse(out Rulewords key))
                    throw new EnException(ErrorCode.Err201);

                switch (key) {
                case Rulewords.IF:
                    if (ruleState != Rulewords.RULE)
                        throw new EnException(ErrorCode.Err221);
                    ruleState = Rulewords.IF;
                    ParsePremise(tok, Rulewords.AND, nodes, links);
                    break;

                case Rulewords.AND:
                    switch (ruleState) {
                    case Rulewords.IF:
                        ParsePremise(tok, Rulewords.AND, nodes, links);
                        break;
                    case Rulewords.THEN:
                    case Rulewords.ELSE:
                        ParseAction(ruleState, tok, links);
                        break;
                    default:
                        throw new EnException(ErrorCode.Err221);
                    }
                    break;

                case Rulewords.OR:
                    if (ruleState == Rulewords.IF)
                        ParsePremise(tok, Rulewords.OR, nodes, links);
                    else
                        throw new EnException(ErrorCode.Err221);
                    break;

                case Rulewords.THEN:
                    if (ruleState != Rulewords.IF)
                        throw new EnException(ErrorCode.Err221);
                    ruleState = Rulewords.THEN;
                    ParseAction(ruleState, tok, links);
                    break;

                case Rulewords.ELSE:
                    if (ruleState != Rulewords.THEN)
                        throw new EnException(ErrorCode.Err221);
                    ruleState = Rulewords.ELSE;
                    ParseAction(ruleState, tok, links);
                    break;

                case Rulewords.PRIORITY: {
                    if (ruleState != Rulewords.THEN && ruleState != Rulewords.ELSE)
                        throw new EnException(ErrorCode.Err221);

                    ruleState = Rulewords.PRIORITY;

                    if (!tok[1].ToDouble(out tempPriority))
                        throw new EnException(ErrorCode.Err202);

                    break;
                }

                default:
                    throw new EnException(ErrorCode.Err201);
                }
            }

            _priority = tempPriority;
        }

        private void ParsePremise(
            string[] tok,
            Rulewords logop,
            IEnumerable<SimulationNode> nodes,
            IEnumerable<SimulationLink> links) {
            
            _pchain.Add(new Premise(tok, logop, nodes, links));

        }

        private void ParseAction(Rulewords state, string[] tok, IEnumerable<SimulationLink> links) {
            Action a = new Action(tok, links, _label);

            if (state == Rulewords.THEN)
                _tchain.Insert(0, a);
            else
                _fchain.Insert(0, a);
        }

    }

}