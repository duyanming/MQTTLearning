using MQTTnet;
using MQTTnet.Client.Connecting;
using MQTTnet.Client.Disconnecting;
using MQTTnet.Diagnostics;
using MQTTnet.Protocol;
using MQTTnet.Server;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MqttServerTest
{
    class Program
    {
        private static IMqttServer mqttServer = null;
        private static List<string> connectedClientId = new List<string>();

        static void Main(string[] args)
        {
            Task.Run(async () => { await StartMqttServer(); });

            // Write all trace messages to the console window.
            MqttNetGlobalLogger.LogMessagePublished += MqttNetTrace_TraceMessagePublished;

            //2.4.0版本
            //MqttNetTrace.TraceMessagePublished += MqttNetTrace_TraceMessagePublished;
            //new Thread(StartMqttServer).Start();
            while (true)
            {
                if (mqttServer==null)
                {
                    Console.WriteLine("Please await mqttServer.StartAsync()");
                    Thread.Sleep(1000);
                    continue;
                }

                var inputString = Console.ReadLine().ToLower().Trim();

                if (inputString == "exit")
                {
                    Task.Run(async () => { await EndMqttServer(); });
                    Console.WriteLine("MQTT服务已停止！");
                    break;
                }
                else if (inputString == "clients")
                {
                    var connectedClients = mqttServer.GetClientStatusAsync();

                    Console.WriteLine($"客户端标识：");
                    //2.4.0
                    //foreach (var item in mqttServer.GetConnectedClients())
                    //{
                    //    Console.WriteLine($"客户端标识：{item.ClientId}，协议版本：{item.ProtocolVersion}");
                    //}
                }
                else if (inputString.StartsWith("hello:"))
                {
                    string msg = inputString.Substring(6);
                    Topic_Hello(msg);
                }
                else if (inputString.StartsWith("control:"))
                {
                    string msg = inputString.Substring(8);
                    Topic_Host_Control(msg);
                }
                else if(inputString.StartsWith("serialize:"))
                {
                    AllData data = new AllData();
                    data.m_data = new EquipmentDataJson();
                    data.m_data.str_test = "host";
                    data.m_data.str_arr_test = new string[3] { "h1", "h2", "h3" };
                    data.m_data.int_test = 5;
                    data.m_data.int_arr_test = new int[5] { 2, 4, 5, 8, 10 };
                    string msg = JsonConvert.SerializeObject(data.m_data);
                    Topic_Serialize(msg);
                }
                else if (inputString.StartsWith("subscribe:"))
                {
                    string msg = inputString.Substring(10);
                    Subscribe(msg);
                }
                else
                {
                    Console.WriteLine($"命令[{inputString}]无效！");
                }
                Thread.Sleep(100);
            }
        }

        private static void MqttServer_ClientConnected(MqttServerClientConnectedEventArgs e)
        {
            Console.WriteLine($"客户端[{e.ClientId}]已连接，协议版本：{e.ClientId}");
            connectedClientId.Add(e.ClientId);
        }

        private static void MqttServer_ClientDisconnected(MqttServerClientDisconnectedEventArgs e)
        {
            Console.WriteLine($"客户端[{e.ClientId}]已断开连接！");
            connectedClientId.Remove(e.ClientId);
        }

        private static void MqttServer_ApplicationMessageReceived( MqttApplicationMessageReceivedEventArgs e)
        {
            string recv = Encoding.UTF8.GetString(e.ApplicationMessage.Payload);
            Console.WriteLine("### RECEIVED APPLICATION MESSAGE ###");
            Console.WriteLine($"客户端[{e.ClientId}]>>");
            Console.WriteLine($"+ Topic = {e.ApplicationMessage.Topic}");
            Console.WriteLine($"+ Payload = {recv}");
            Console.WriteLine($"+ QoS = {e.ApplicationMessage.QualityOfServiceLevel}");
            Console.WriteLine($"+ Retain = {e.ApplicationMessage.Retain}");
            Console.WriteLine();
            if (e.ApplicationMessage.Topic== "slave/json")
            {
                JsonData(recv);
            }
        }

        private static void MqttNetTrace_TraceMessagePublished(object sender, MqttNetLogMessagePublishedEventArgs e)
        {
            var trace = $">> [{e.TraceMessage.Timestamp:O}] [{e.TraceMessage.ThreadId}] [{e.TraceMessage.Source}] [{e.TraceMessage.Level}]: {e.TraceMessage.Message}";
            if (e.TraceMessage.Exception != null)
            {
                trace += Environment.NewLine + e.TraceMessage.Exception.ToString();
            }

            Console.WriteLine(trace);
        }

        #region 3.0.8

        private static async Task StartMqttServer()
        {
            if (mqttServer == null)
            {
                // Configure MQTT server.
                var optionsBuilder = new MqttServerOptionsBuilder()
                    .WithConnectionBacklog(100)
                    .WithDefaultEndpointPort(8222)
                    .WithConnectionValidator(c =>
                    {
                        Dictionary<string, string> c_u = new Dictionary<string, string>();
                        c_u.Add("client001", "username001");
                        c_u.Add("client002", "username002");
                        Dictionary<string, string> u_psw = new Dictionary<string, string>();
                        u_psw.Add("username001", "psw001");
                        u_psw.Add("username002", "psw002");

                        if (c_u.ContainsKey(c.ClientId) && c_u[c.ClientId] == c.Username)
                        {
                            if (u_psw.ContainsKey(c.Username) && u_psw[c.Username] == c.Password)
                            {
                                c.ReturnCode = MqttConnectReturnCode.ConnectionAccepted;
                            }
                            else
                            {
                                c.ReturnCode = MqttConnectReturnCode.ConnectionRefusedBadUsernameOrPassword;
                            }
                        }
                        else
                        {
                            c.ReturnCode = MqttConnectReturnCode.ConnectionRefusedIdentifierRejected;
                        }
                    })                    ;

                // Start a MQTT server.
                mqttServer = new MqttFactory().CreateMqttServer();
                mqttServer.UseApplicationMessageReceivedHandler( MqttServer_ApplicationMessageReceived);
                mqttServer.UseClientConnectedHandler(MqttServer_ClientConnected);
                mqttServer.UseClientDisconnectedHandler( MqttServer_ClientDisconnected);

                _ = mqttServer.StartAsync(optionsBuilder.Build());
                Console.WriteLine("MQTT服务启动成功！");
            }
        }

        private static async Task EndMqttServer()
        {
            if (mqttServer!=null)
            {
                await mqttServer.StopAsync();
            }
            else
            {
                Console.WriteLine("mqttserver=null");
            }
        }
        
        private static void Usingcertificate(ref MqttServerOptions options)
        {
            var certificate = new X509Certificate(@"C:\certs\test\test.cer", "");
            options.TlsEndpointOptions.Certificate = certificate.Export(X509ContentType.Cert);
            var aes = new System.Security.Cryptography.AesManaged();

        }

        #endregion

        #region Topic

        private static async void Topic_Hello(string msg)
        {
            string topic = "topic/hello";

            //2.4.0版本的
            //var appMsg = new MqttApplicationMessage(topic, Encoding.UTF8.GetBytes(inputString), MqttQualityOfServiceLevel.AtMostOnce, false);
            //mqttClient.PublishAsync(appMsg);

            var message = new MqttApplicationMessageBuilder()
                .WithTopic(topic)
                .WithPayload(msg)
                .WithAtMostOnceQoS()
                .WithRetainFlag()
                .Build();
            await mqttServer.PublishAsync(message);
        }

        private static async void Topic_Host_Control(string msg)
        {
            string topic = "topic/host/control";

            var message = new MqttApplicationMessageBuilder()
                .WithTopic(topic)
                .WithPayload(msg)
                .WithAtMostOnceQoS()
                .WithRetainFlag(false)
                .Build();
            await mqttServer.PublishAsync(message);
        }

        private static async void Topic_Serialize(string msg)
        {
            string topic = "topic/serialize";
            
            var message = new MqttApplicationMessageBuilder()
                .WithTopic(topic)
                .WithPayload(msg)
                .WithAtMostOnceQoS()
                .WithRetainFlag(false)
                .Build();
            await mqttServer.PublishAsync(message);
        }

        /// <summary>
        /// 替指定的clientID订阅指定的内容
        /// </summary>
        /// <param name="topic"></param>
        private static void Subscribe(string topic)
        {
            List<TopicFilter> topicFilter = new List<TopicFilter>();
            topicFilter.Add(new TopicFilterBuilder()
                .WithTopic(topic)
                .WithAtMostOnceQoS()
                .Build());
            //给"client001"订阅了主题为topicFilter的payload
            mqttServer.SubscribeAsync("client001", topicFilter);
            Console.WriteLine($"Subscribe:[{"client001"}]，Topic：{topic}");
        }

        #endregion

        #region JsonSerialize

        private static void JsonData(string recvPayload)
        {
            AllData data = new AllData();
            data.m_data = (EquipmentDataJson)JsonConvert.DeserializeObject(recvPayload, typeof(EquipmentDataJson));
            Console.Write($"recv: str_test={data.m_data.str_test}, str_arr_test={data.m_data.str_arr_test}");
            Console.Write($"recv: int_test={data.m_data.int_test}, int_arr_test={data.m_data.int_arr_test}");
            Console.WriteLine("");
        }

        #endregion
    }
}