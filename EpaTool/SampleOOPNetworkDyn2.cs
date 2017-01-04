﻿using System;
using System.Diagnostics;
using System.IO;

using Epanet.Enums;
using Epanet.Hydraulic;
using Epanet.Hydraulic.Structures;
using Epanet.Network.IO.Input;
using Epanet.Network.Structures;
using Epanet.Util;

using EpanetNetwork = Epanet.Network.Network;

namespace Epanet {

    public class SampleOOPNetworkDyn2 {
        public static void main(string[] args) {

            var net = new EpanetNetwork();

            //Tank
            Tank tank = new Tank("0") {Elevation = 210};
            net.Nodes.Add(tank);

            //Nodes
            Node[] node = new Node[7];
            for (int i = 1; i < 7; i++) {
                node[i] = new Node(i.ToString());
            }

            node[1].Elevation = 150;
            node[1].InitDemand = 100;
            node[2].Elevation = 160;
            node[2].InitDemand = 100;
            node[3].Elevation = 155;
            node[3].InitDemand = 120;
            node[4].Elevation = 150;
            node[4].InitDemand = 270;
            node[5].Elevation = 165;
            node[5].InitDemand = 330;
            node[6].Elevation = 160;
            node[6].InitDemand = 200;

            //Links
            Link[] pipe = new Link[8];
            for (int i = 0; i < 8; i++) {
                pipe[i] = new Link(i.ToString()) {Lenght = 1000};
            }

            pipe[0].FirstNode = tank;
            pipe[0].SecondNode = node[1];
            pipe[1].FirstNode = node[1];
            pipe[1].SecondNode = node[2];
            pipe[2].FirstNode = node[1];
            pipe[2].SecondNode = node[3];
            pipe[3].FirstNode = node[3];
            pipe[3].SecondNode = node[4];
            pipe[4].FirstNode = node[3];
            pipe[4].SecondNode = node[5];
            pipe[5].FirstNode = node[5];
            pipe[5].SecondNode = node[6];
            pipe[6].FirstNode = node[2];
            pipe[6].SecondNode = node[4];
            pipe[7].FirstNode = node[4];
            pipe[7].SecondNode = node[6];

            for (int i = 1; i < 7; i++) {
                net.Nodes.Add(node[i]);
            }

            for (int i = 0; i < 8; i++) {
                net.Links.Add(pipe[i]);
            }


            //Prepare Network
            TraceSource log = new TraceSource(typeof(SampleOOPNetwork2).FullName, SourceLevels.All);
            NullParser nP = (NullParser)InputParser.Create(FileType.NULL_FILE, log);
            Debug.Assert(nP != null);
            nP.Parse(net, null);

            //// Simulate hydraulics and get streaming/dynamic results
            new DynamicSimulation(net, log).Simulate();
        }

        class DynamicSimulation:HydraulicSim {

            public DynamicSimulation(EpanetNetwork net, TraceSource log) : base(net, log) { }

            public void Simulate() { base.Simulate((BinaryWriter)null); }

            protected override long NextHyd() {
                long l1 = base.NextHyd();
                Console.Write("Time : " + l1.GetClockTime() + ", nodes heads : ");
                var fmap = base.Net.FieldsMap;

                foreach (SimulationNode node in base.Nodes) {
                    double H = fmap.RevertUnit(FieldType.HEAD, node.SimHead);
                    Console.Write("{0:f2}\t", H);
                }
                Console.WriteLine();
                return l1;
            }
        }

    }

}
