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
using Epanet.Log;
using Epanet.Network;
using Epanet.Network.Structures;
using Epanet.Util;

namespace Epanet.Hydraulic.Structures {

    public class SimulationRule {

        /// <summary>Temporary action item</summary>
        private class ActItem {
            public ActItem(SimulationRule rule, Action action) {
                this.Rule = rule;
                this.Action = action;
            }

            public SimulationRule Rule;
            public Action Action;
        }

        /// <summary>Rule premise.</summary>
        private class Premise {
            public Premise(string[] tok, Rule.Rulewords lOp, List<SimulationNode> nodes, List<SimulationLink> links) {
                Rule.Objects loType;
                Rule.Varwords lVar;
                object lObj;
                Rule.Operators lROp;

                if (tok.Length != 5 && tok.Length != 6)
                    throw new ENException(ErrorCode.Err201);

                EnumsTxt.TryParse(tok[1], out loType);

                if (loType == Rule.Objects.r_SYSTEM) {
                    EnumsTxt.TryParse(tok[2], out lVar);

                    if (lVar != Rule.Varwords.r_DEMAND && lVar != Rule.Varwords.r_TIME &&
                        lVar != Rule.Varwords.r_CLOCKTIME)
                        throw new ENException(ErrorCode.Err201);

                    lObj = Rule.Objects.r_SYSTEM;
                }
                else {
                    if (!EnumsTxt.TryParse(tok[3], out lVar))
                        throw new ENException(ErrorCode.Err201);

                    switch (loType) {
                    case Rule.Objects.r_NODE:
                    case Rule.Objects.r_JUNC:
                    case Rule.Objects.r_RESERV:
                    case Rule.Objects.r_TANK:
                        loType = Rule.Objects.r_NODE;
                        break;
                    case Rule.Objects.r_LINK:
                    case Rule.Objects.r_PIPE:
                    case Rule.Objects.r_PUMP:
                    case Rule.Objects.r_VALVE:
                        loType = Rule.Objects.r_LINK;
                        break;
                    default:
                        throw new ENException(ErrorCode.Err201);
                    }

                    if (loType == Rule.Objects.r_NODE) {
                        //Node nodeRef = net.getNode(Tok[2]);
                        SimulationNode nodeRef = null;
                        foreach (SimulationNode simNode  in  nodes)
                            if (simNode.Node.Id.Equals(tok[2], StringComparison.OrdinalIgnoreCase))
                                nodeRef = simNode;

                        if (nodeRef == null)
                            throw new ENException(ErrorCode.Err203);
                        switch (lVar) {
                        case Rule.Varwords.r_DEMAND:
                        case Rule.Varwords.r_HEAD:
                        case Rule.Varwords.r_GRADE:
                        case Rule.Varwords.r_LEVEL:
                        case Rule.Varwords.r_PRESSURE:
                            break;
                        case Rule.Varwords.r_FILLTIME:
                        case Rule.Varwords.r_DRAINTIME:
                            if (nodeRef is SimulationTank)
                                throw new ENException(ErrorCode.Err201);
                            break;

                        default:
                            throw new ENException(ErrorCode.Err201);
                        }
                        lObj = nodeRef;
                    }
                    else {
                        //Link linkRef = net.getLink(Tok[2]);
                        SimulationLink linkRef = null;
                        foreach (SimulationLink simLink  in  links)
                            if (simLink.Link.Id.Equals(tok[2], StringComparison.OrdinalIgnoreCase))
                                linkRef = simLink;

                        if (linkRef == null)
                            throw new ENException(ErrorCode.Err204);
                        switch (lVar) {
                        case Rule.Varwords.r_FLOW:
                        case Rule.Varwords.r_STATUS:
                        case Rule.Varwords.r_SETTING:
                            break;
                        default:
                            throw new ENException(ErrorCode.Err201);
                        }
                        lObj = linkRef;
                    }
                }

                Rule.Operators op;

                if (!EnumsTxt.TryParse(loType == Rule.Objects.r_SYSTEM ? tok[3] : tok[4], out op))
                    throw new ENException(ErrorCode.Err201);

                switch (op) {
                case Rule.Operators.IS:
                    lROp = Rule.Operators.EQ;
                    break;
                case Rule.Operators.NOT:
                    lROp = Rule.Operators.NE;
                    break;
                case Rule.Operators.BELOW:
                    lROp = Rule.Operators.LT;
                    break;
                case Rule.Operators.ABOVE:
                    lROp = Rule.Operators.GT;
                    break;
                default:
                    lROp = op;
                    break;
                }

                // BUG: Baseform bug lStat == Rule.Values.IS_NUMBER
                Rule.Values lStat = Rule.Values.IS_NUMBER;
                double lVal = Constants.MISSING;

                if (lVar == Rule.Varwords.r_TIME || lVar == Rule.Varwords.r_CLOCKTIME) {
                    if (tok.Length == 6)
                        lVal = Utilities.GetHour(tok[4], tok[5]) * 3600.0;
                    else
                        lVal = Utilities.GetHour(tok[4], "") * 3600.0;

                    if (lVal < 0.0)
                        throw new ENException(ErrorCode.Err202);
                }
                else {
                    Rule.Values k;

                    if (!EnumsTxt.TryParse(tok[tok.Length - 1], out k) || lStat <= Rule.Values.IS_NUMBER) {
                        if (lStat == (Rule.Values)(-1) || lStat <= Rule.Values.IS_NUMBER) {
                            if (!tok[tok.Length - 1].ToDouble(out lVal))
                                throw new ENException(ErrorCode.Err202);

                            if (lVar == Rule.Varwords.r_FILLTIME || lVar == Rule.Varwords.r_DRAINTIME)
                                lVal = lVal * 3600.0;
                        }
                    }
                    else {
                        lStat = k;
                    }

                }

                this.status = lStat;
                this.value = lVal;
                this.logop = lOp;
                this.relop = lROp;
                this.variable = lVar;
                this.@object = lObj;
            }

            /// <summary>Logical operator</summary>
            public readonly Rule.Rulewords logop;
            private readonly object @object;
            /// <summary>Pressure, flow, etc</summary>
            private readonly Rule.Varwords variable;
            /// <summary>Relational operator</summary>
            private readonly Rule.Operators relop;
            /// <summary>Variable's status</summary>
            private readonly Rule.Values status;
            /// <summary>Variable's value</summary>
            private readonly double value;

            /// <summary>Checks if a particular premise is true.</summary>
            public bool CheckPremise(
                FieldsMap fMap,
                PropertiesMap pMap,
                long time1,
                long htime,
                double dsystem) {
                if (this.variable == Rule.Varwords.r_TIME || this.variable == Rule.Varwords.r_CLOCKTIME)
                    return this.CheckTime(pMap, time1, htime);
                else if (this.status > Rule.Values.IS_NUMBER)
                    return this.CheckStatus();
                else
                    return this.CheckValue(fMap, dsystem);
            }

            /// <summary>Checks if condition on system time holds.</summary>
            private bool CheckTime(PropertiesMap pMap, long time1, long htime) {
                long t1, t2;

                if (this.variable == Rule.Varwords.r_TIME) {
                    t1 = time1;
                    t2 = htime;
                }
                else if (this.variable == Rule.Varwords.r_CLOCKTIME) {
                    t1 = (time1 + pMap.Tstart) % Constants.SECperDAY;
                    t2 = (htime + pMap.Tstart) % Constants.SECperDAY;
                }
                else
                    return false;

                var x = (long)this.value;
                switch (this.relop) {
                case Rule.Operators.LT:
                    if (t2 >= x) return false;
                    break;
                case Rule.Operators.LE:
                    if (t2 > x) return false;
                    break;
                case Rule.Operators.GT:
                    if (t2 <= x) return false;
                    break;
                case Rule.Operators.GE:
                    if (t2 < x) return false;
                    break;

                case Rule.Operators.EQ:
                case Rule.Operators.NE:
                    var flag = false;
                    if (t2 < t1) {
                        if (x >= t1 || x <= t2) flag = true;
                    }
                    else {
                        if (x >= t1 && x <= t2) flag = true;
                    }
                    if (this.relop == Rule.Operators.EQ && !flag) return true;
                    if (this.relop == Rule.Operators.NE && flag) return true;
                    break;
                }

                return true;
            }

            /// <summary>Checks if condition on link status holds.</summary>
            private bool CheckStatus() {
                switch (this.status) {
                case Rule.Values.IS_OPEN:
                case Rule.Values.IS_CLOSED:
                case Rule.Values.IS_ACTIVE:
                    Rule.Values j;
                    var simlink = this.@object as SimulationLink;
                    Link.StatType i = simlink == null ? (Link.StatType)(-1) : simlink.SimStatus;

                    if(i >= Link.StatType.XHEAD && i <= Link.StatType.CLOSED)
                        j = Rule.Values.IS_CLOSED;
                    else if (i == Link.StatType.ACTIVE)
                        j = Rule.Values.IS_ACTIVE;
                    else
                        j = Rule.Values.IS_OPEN;

                    if (j == this.status && this.relop == Rule.Operators.EQ)
                        return true;
                    if (j != this.status && this.relop == Rule.Operators.NE)
                        return true;
                    break;
                }
                return false;
            }

            /// <summary>Checks if numerical condition on a variable is true.</summary>
            private bool CheckValue(FieldsMap fMap, double dsystem) {
                const double tol = 0.001D;
                double x;

                SimulationLink link = this.@object as SimulationLink;
                SimulationNode node = this.@object as SimulationNode;


                switch (this.variable) {
                case Rule.Varwords.r_DEMAND:
                    if ((Rule.Objects)this.@object == Rule.Objects.r_SYSTEM)
                        x = dsystem * fMap.GetUnits(FieldsMap.FieldType.DEMAND);
                    else
                        x = node.SimDemand * fMap.GetUnits(FieldsMap.FieldType.DEMAND);
                    break;

                case Rule.Varwords.r_HEAD:
                case Rule.Varwords.r_GRADE:
                    x = node.SimHead * fMap.GetUnits(FieldsMap.FieldType.HEAD);
                    break;

                case Rule.Varwords.r_PRESSURE:
                    x = (node.SimHead - node.Elevation) * fMap.GetUnits(FieldsMap.FieldType.PRESSURE);
                    break;

                case Rule.Varwords.r_LEVEL:
                    x = (node.SimHead - node.Elevation) * fMap.GetUnits(FieldsMap.FieldType.HEAD);
                    break;

                case Rule.Varwords.r_FLOW:
                    x = Math.Abs(link.SimFlow) * fMap.GetUnits(FieldsMap.FieldType.FLOW);
                    break;

                case Rule.Varwords.r_SETTING:

                    if (link.SimSetting == Constants.MISSING)
                        return false;

                    x = link.SimSetting;
                    switch (link.Type) {
                    case Link.LinkType.PRV:
                    case Link.LinkType.PSV:
                    case Link.LinkType.PBV:
                        x = x * fMap.GetUnits(FieldsMap.FieldType.PRESSURE);
                        break;
                    case Link.LinkType.FCV:
                        x = x * fMap.GetUnits(FieldsMap.FieldType.FLOW);
                        break;
                    }
                    break;
                case Rule.Varwords.r_FILLTIME: {
                    if (!(this.@object is SimulationTank))
                        return false;

                    SimulationTank tank = (SimulationTank)this.@object;

                    if (tank.IsReservoir)
                        return false;

                    if (tank.SimDemand <= Constants.TINY)
                        return false;

                    x = (tank.Vmax - tank.SimVolume) / tank.SimDemand;

                    break;
                }
                case Rule.Varwords.r_DRAINTIME: {
                    if (!(this.@object is SimulationTank))
                        return false;

                    SimulationTank tank = (SimulationTank)this.@object;

                    if (tank.IsReservoir)
                        return false;

                    if (tank.SimDemand >= -Constants.TINY)
                        return false;

                    x = (tank.Vmin - tank.SimVolume) / tank.SimDemand;
                    break;
                }
                default:
                    return false;
                }

                switch (this.relop) {
                case Rule.Operators.EQ:
                    if (Math.Abs(x - this.value) > tol)
                        return false;
                    break;
                case Rule.Operators.NE:
                    if (Math.Abs(x - this.value) < tol)
                        return false;
                    break;
                case Rule.Operators.LT:
                    if (x > this.value + tol)
                        return false;
                    break;
                case Rule.Operators.LE:
                    if (x > this.value - tol)
                        return false;
                    break;
                case Rule.Operators.GT:
                    if (x < this.value - tol)
                        return false;
                    break;
                case Rule.Operators.GE:
                    if (x < this.value + tol)
                        return false;
                    break;
                }
                return true;
            }

        }

        private class Action {
            private readonly string _label;

            public Action(string[] tok, List<SimulationLink> links, string label) {
                this._label = label;

                int ntokens = tok.Length;

                Rule.Values k;

                if (ntokens != 6)
                    throw new ENException(ErrorCode.Err201);

                //Link linkRef = net.getLink(tok[2]);
                SimulationLink linkRef = null;
                foreach (SimulationLink simLink  in  links)
                    if (simLink.Link.Id.Equals(tok[2], StringComparison.OrdinalIgnoreCase))
                        linkRef = simLink;

                if (linkRef == null)
                    throw new ENException(ErrorCode.Err204);

                if (linkRef.Type == Link.LinkType.CV)
                    throw new ENException(ErrorCode.Err207);

                var s = (Rule.Values)(-1);
                double x = Constants.MISSING;

                if (EnumsTxt.TryParse(tok[5], out k) && k > Rule.Values.IS_NUMBER) {
                    s = k;
                }
                else {
                    if (!tok[5].ToDouble(out x) || x < 0.0)
                        throw new ENException(ErrorCode.Err202);
                }

                if (x != Constants.MISSING && linkRef.Type == Link.LinkType.GPV)
                    throw new ENException(ErrorCode.Err202);

                if (x != Constants.MISSING && linkRef.Type == Link.LinkType.PIPE) {
                    s = x == 0.0 ? Rule.Values.IS_CLOSED : Rule.Values.IS_OPEN;
                    x = Constants.MISSING;
                }

                this.link = linkRef;
                this.status = s;
                this.setting = x;
            }

            public readonly SimulationLink link;
            private readonly Rule.Values status;
            private readonly double setting;

            /// <summary>Execute action, returns true if the link was alterated.</summary>
            public bool Execute(FieldsMap fMap, PropertiesMap pMap, TraceSource log, double tol, long htime) {
                bool flag = false;

                Link.StatType s = this.link.SimStatus;
                double v = this.link.SimSetting;
                double x = this.setting;

                if (this.status == Rule.Values.IS_OPEN && s <= Link.StatType.CLOSED) {
                    // Switch link from closed to open
                    this.link.SetLinkStatus(true);
                    flag = true;
                }
                else if (this.status == Rule.Values.IS_CLOSED && s > Link.StatType.CLOSED) {
                    // Switch link from not closed to closed
                    this.link.SetLinkStatus(false);
                    flag = true;
                }
                else if (x != Constants.MISSING) {
                    // Change link's setting
                    switch (this.link.Type) {
                    case Link.LinkType.PRV:
                    case Link.LinkType.PSV:
                    case Link.LinkType.PBV:
                        x = x / fMap.GetUnits(FieldsMap.FieldType.PRESSURE);
                        break;
                    case Link.LinkType.FCV:
                        x = x / fMap.GetUnits(FieldsMap.FieldType.FLOW);
                        break;
                    }
                    if (Math.Abs(x - v) > tol) {
                        this.link.SetLinkSetting(x);
                        flag = true;
                    }
                }

                if (flag) {
                    if (pMap.Statflag > 0) // Report rule action
                        this.LogRuleExecution(log, htime);
                    return true;
                }

                return false;
            }

            private void LogRuleExecution(TraceSource log, long htime) {
                log.Warning(
                    Properties.Text.ResourceManager.GetString("FMT63"),
                    htime.GetClockTime(),
                    this.link.Type.ParseStr(),
                    this.link.Link.Id,
                    this._label);
            }
        }


        private readonly string label;
        private readonly double priority;
        private readonly List<Premise> pchain = new List<Premise>();
        private readonly List<Action> tchain = new List<Action>();
        private readonly List<Action> fchain = new List<Action>();


        // Simulation Methods


        /// <summary>Evaluate rule premises.</summary>
        private bool EvalPremises(
            FieldsMap fMap,
            PropertiesMap pMap,
            long time1,
            long htime,
            double dsystem) {
            bool result = true;

            foreach (var p  in  this.pchain) {
                if (p.logop == Rule.Rulewords.r_OR) {
                    if (!result)
                        result = p.CheckPremise(fMap, pMap, time1, htime, dsystem);
                }
                else {
                    if (!result)
                        return false;
                    result = p.CheckPremise(fMap, pMap, time1, htime, dsystem);
                }

            }
            return result;
        }

        /// <summary>Adds rule's actions to action list.</summary>
        private static void UpdateActionList(SimulationRule rule, List<ActItem> actionList, bool branch) {
            if (branch) {
                // go through the true action branch
                foreach (Action a  in  rule.tchain) {
                    if (!CheckAction(rule, a, actionList)) // add a new action from the "true" chain
                        actionList.Add(new ActItem(rule, a));
                }
            }
            else {
                foreach (Action a  in  rule.fchain) {
                    if (!CheckAction(rule, a, actionList)) // add a new action from the "false" chain
                        actionList.Add(new ActItem(rule, a));
                }
            }
        }

        /// <summary>Checks if an action with the same link is already on the Action List.</summary>
        private static bool CheckAction(SimulationRule rule, Action action, List<ActItem> actionList) {

            foreach (ActItem item  in  actionList) {
                if (item.Action.link == action.link) {
                    // Action with same link
                    if (rule.priority > item.Rule.priority) {
                        // Replace Actitem action with higher priority rule
                        item.Rule = rule;
                        item.Action = action;
                    }

                    return true;
                }
            }

            return false;
        }

        /// <summary>Implements actions on action list, returns the number of actions executed.</summary>
        private static int TakeActions(
            FieldsMap fMap,
            PropertiesMap pMap,
            TraceSource log,
            List<ActItem> actionList,
            long htime) {
            double tol = 1e-3;
            int n = 0;

            foreach (ActItem item  in  actionList) {
                if (item.Action.Execute(fMap, pMap, log, tol, htime))
                    n++;
            }

            return n;
        }


        /// <summary>Checks which rules should fire at current time.</summary>
        private static int Check(
            FieldsMap fMap,
            PropertiesMap pMap,
            List<SimulationRule> rules,
            TraceSource log,
            long htime,
            long dt,
            double dsystem) {
            // Start of rule evaluation time interval
            long time1 = htime - dt + 1;

            List<ActItem> actionList = new List<ActItem>();

            foreach (SimulationRule rule  in  rules)
                UpdateActionList(rule, actionList, rule.EvalPremises(fMap, pMap, time1, htime, dsystem));

            return TakeActions(fMap, pMap, log, actionList, htime);
        }

        /// <summary>
        /// Updates next time step by checking if any rules will fire before then; 
        /// also updates tank levels.
        /// </summary>
        public static void MinimumTimeStep(
            FieldsMap fMap,
            PropertiesMap pMap,
            TraceSource log,
            List<SimulationRule> rules,
            List<SimulationTank> tanks,
            long htime,
            long tstep,
            double dsystem,
            out long tstepOut,
            out long htimeOut) {

            long dt; // Normal time increment for rule evaluation
            long dt1; // Actual time increment for rule evaluation

            // Find interval of time for rule evaluation
            long tnow = htime;        // Start of time interval for rule evaluation
            long tmax = tnow + tstep; // End of time interval for rule evaluation

            //If no rules, then time increment equals current time step
            if (rules.Count == 0) {
                dt = tstep;
                dt1 = dt;
            }
            else {
                // Otherwise, time increment equals rule evaluation time step and
                // first actual increment equals time until next even multiple of
                // Rulestep occurs.
                dt = pMap.Rulestep;
                dt1 = pMap.Rulestep - tnow % pMap.Rulestep;
            }

            // Make sure time increment is no larger than current time step
            dt = Math.Min(dt, tstep);
            dt1 = Math.Min(dt1, tstep);

            if (dt1 == 0)
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
                SimulationTank.StepWaterLevels(tanks, fMap, dt1); // Find new tank levels
                if (Check(fMap, pMap, rules, log, htime, dt1, dsystem) != 0) break; // Stop if rules fire
                dt = Math.Min(dt, tmax - htime); // Update time increment
                dt1 = dt; // Update actual increment
            }
            while (dt > 0);

            //Compute an updated simulation time step (*tstep)
            // and return simulation time to its original value
            tstepOut = htime - tnow;
            htimeOut = tnow;

        }

        public SimulationRule(Rule rule, List<SimulationLink> links, List<SimulationNode> nodes) {
            this.label = rule.Label;

            double tempPriority = 0.0;

            Rule.Rulewords ruleState = Rule.Rulewords.r_RULE;

            foreach (string line in rule.Code) {
                string[] tok = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                Rule.Rulewords key;

                if (!EnumsTxt.TryParse(tok[0], out key))
                    throw new ENException(ErrorCode.Err201);

                switch (key) {
                case Rule.Rulewords.r_IF:
                    if (ruleState != Rule.Rulewords.r_RULE)
                        throw new ENException(ErrorCode.Err221);
                    ruleState = Rule.Rulewords.r_IF;
                    this.ParsePremise(tok, Rule.Rulewords.r_AND, nodes, links);
                    break;

                case Rule.Rulewords.r_AND:
                    if (ruleState == Rule.Rulewords.r_IF)
                        this.ParsePremise(tok, Rule.Rulewords.r_AND, nodes, links);
                    else if (ruleState == Rule.Rulewords.r_THEN || ruleState == Rule.Rulewords.r_ELSE)
                        this.ParseAction(ruleState, tok, links);
                    else
                        throw new ENException(ErrorCode.Err221);
                    break;

                case Rule.Rulewords.r_OR:
                    if (ruleState == Rule.Rulewords.r_IF)
                        this.ParsePremise(tok, Rule.Rulewords.r_OR, nodes, links);
                    else
                        throw new ENException(ErrorCode.Err221);
                    break;

                case Rule.Rulewords.r_THEN:
                    if (ruleState != Rule.Rulewords.r_IF)
                        throw new ENException(ErrorCode.Err221);
                    ruleState = Rule.Rulewords.r_THEN;
                    this.ParseAction(ruleState, tok, links);
                    break;

                case Rule.Rulewords.r_ELSE:
                    if (ruleState != Rule.Rulewords.r_THEN)
                        throw new ENException(ErrorCode.Err221);
                    ruleState = Rule.Rulewords.r_ELSE;
                    this.ParseAction(ruleState, tok, links);
                    break;

                case Rule.Rulewords.r_PRIORITY: {
                    if (ruleState != Rule.Rulewords.r_THEN && ruleState != Rule.Rulewords.r_ELSE)
                        throw new ENException(ErrorCode.Err221);

                    ruleState = Rule.Rulewords.r_PRIORITY;

                    if (!tok[1].ToDouble(out tempPriority))
                        throw new ENException(ErrorCode.Err202);

                    break;
                }

                default:
                    throw new ENException(ErrorCode.Err201);
                }
            }

            this.priority = tempPriority;
        }

        private void ParsePremise(
            string[] tok,
            Rule.Rulewords logop,
            List<SimulationNode> nodes,
            List<SimulationLink> links) {
            Premise p = new Premise(tok, logop, nodes, links);
            this.pchain.Add(p);

        }

        private void ParseAction(Rule.Rulewords state, string[] tok, List<SimulationLink> links) {
            Action a = new Action(tok, links, this.label);

            if (state == Rule.Rulewords.r_THEN)
                this.tchain.Insert(0, a);
            else
                this.fchain.Insert(0, a);
        }

    }

}