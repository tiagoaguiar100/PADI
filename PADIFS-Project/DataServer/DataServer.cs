﻿using SharedLibrary;
using SharedLibrary.Entities;
using SharedLibrary.Exceptions;
using SharedLibrary.Interfaces;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Remoting;
using System.Runtime.Remoting.Channels;
using System.Runtime.Remoting.Channels.Tcp;
using System.Threading;

// @TODO Fix primary decision

namespace DataServer
{
    public class DataServer : MarshalByRefObject, IDataServerToPM, IDataServerToMetadata, IDataServerToClient
    {
        class Primary
        {
            public string Id;
            public IMetadataToDataServer Metadata;
        };

        // localFilename / FileData
        private static ConcurrentDictionary<string, FileData> files = new ConcurrentDictionary<string, FileData>();

        // id / interface
        private static ConcurrentDictionary<string, IMetadataToDataServer> metadatas
            = new ConcurrentDictionary<string, IMetadataToDataServer>();
        private static Primary primary = new Primary() { Id = null, Metadata = null };

        private volatile static string id;
        private volatile static int port;

        private volatile static bool fail = false;
        private volatile static bool freeze = false;
        private static ConcurrentQueue<Action> freezed = new ConcurrentQueue<Action>();

        public static void Main(string[] args)
        {
            if (args.Length != 2)
                throw new Exception("Wrong Arguments");

            id = args[0];
            port = Convert.ToInt32(args[1]);

            Console.SetWindowSize(Helper.WINDOW_WIDTH, Helper.WINDOW_HEIGHT);
            Console.Title = id;

            TcpChannel channel = new TcpChannel(port);
            ChannelServices.RegisterChannel(channel, true);
            RemotingConfiguration.RegisterWellKnownServiceType(
                typeof(DataServer),
                id,
                WellKnownObjectMode.Singleton);

            Console.WriteLine("Data Server " + id + " has started.");
            Console.ReadLine();
        }

        public void SendHeartbeat()
        {
            Heartbeat heartbeat = new Heartbeat();
            primary.Metadata.Heartbeat(id, heartbeat);
        }

        /**
         * IDataServerToPM Methods
         */

        // skips failed and freeze
        public void MetadataLocation(string id, string location)
        {
            Console.WriteLine("RECEIVED METADATA LOCATION " + location);

            IMetadataToDataServer metadata = (IMetadataToDataServer)Activator.GetObject(
                typeof(IMetadataToDataServer),
                location);
            metadatas[id] = metadata;

            if (primary.Id == null)
            {
                primary.Id = id;
                primary.Metadata = metadata;
                primary.Metadata.RegisterDataServer(DataServer.id, Helper.GetUrl(DataServer.id, DataServer.port));
            }
        }

        // skips failed and freeze
        public void Dump()
        {
            Console.WriteLine("DUMP");
            Console.WriteLine("Opened File Datas");
            foreach (var entry in files)
            {
                string filename = entry.Key;
                FileData fileData = entry.Value;
                Console.WriteLine(filename + ":" + fileData);
            }
        }

        public void Fail()
        {
            if (freeze)
            {
                freezed.Enqueue(() => Fail());
                throw new ProcessFreezedException(id);
            }
            Console.WriteLine("FAIL");
            fail = true;
        }

        public void Recover()
        {
            if (freeze)
            {
                freezed.Enqueue(() => Recover());
                throw new ProcessFreezedException(id);
            }

            Console.WriteLine("RECOVER");
            fail = false;
        }

        public void Freeze()
        {
            if (fail) throw new ProcessFailedException(id);

            Console.WriteLine("FREEZE");
            freeze = true;
        }

        public void Unfreeze()
        {
            if (fail) throw new ProcessFailedException(id);

            Console.WriteLine("UNFREEZE");

            freeze = false;
            while (freezed.Any())
            {
                Thread pending = new Thread(() =>
                {
                    Action action;
                    if (freezed.TryDequeue(out action)) action();
                });
                pending.Start();
            }
        }

        /**
         * IDataServerToMetadata Methods
         */

        public void Create(string localFilename)
        {
            if (fail) throw new ProcessFailedException(id);
            if (freeze)
            {
                freezed.Enqueue(() => Create(localFilename));
                throw new ProcessFreezedException(id);
            }

            Console.WriteLine("CREATE FILE " + localFilename);

            if (!files.ContainsKey(localFilename))
            {
                files[localFilename] = new FileData();
            }
        }

        public void Delete(string localFilename)
        {
            if (fail) throw new ProcessFailedException(id);
            if (freeze)
            {
                freezed.Enqueue(() => Delete(localFilename));
                throw new ProcessFreezedException(id);
            }

            Console.WriteLine("DELETE FILE " + localFilename);

            if (files.ContainsKey(localFilename))
            {
                FileData ignored; files.TryRemove(localFilename, out ignored);
            }
        }

        /**
         * IDataServerToClient Methods
         */

        public FileVersion Version(string localFilename)
        {
            if (fail) throw new ProcessFailedException(id);
            if (freeze)
            {
                freezed.Enqueue(() => Version(localFilename));
                throw new ProcessFreezedException(id);
            }

            Console.WriteLine("VERSION " + localFilename);

            if (!files.ContainsKey(localFilename))
                throw new FileDoesNotExistException(localFilename);

            return files[localFilename].Version;
        }

        public FileData Read(string localFilename)
        {
            if (fail) throw new ProcessFailedException(id);
            if (freeze)
            {
                freezed.Enqueue(() => Read(localFilename));
                throw new ProcessFreezedException(id);
            }

            Console.WriteLine("READ " + localFilename);

            if (!files.ContainsKey(localFilename))
                throw new FileDoesNotExistException(localFilename);

            return files[localFilename];
        }

        public void Write(string localFilename, FileData newFile)
        {
            if (fail) throw new ProcessFailedException(id);
            if (freeze)
            {
                freezed.Enqueue(() => Write(localFilename, newFile));
                throw new ProcessFreezedException(id);
            }

            Console.WriteLine("WRITE " + localFilename);

            if (!files.ContainsKey(localFilename))
                throw new FileDoesNotExistException(localFilename);

            FileData currentFile = files[localFilename];
            files[localFilename] = FileData.Latest(currentFile, newFile);
        }
    }
}
