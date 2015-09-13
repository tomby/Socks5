﻿using socks5.Encryption;
using socks5.Socks;
using socks5.TCP;
using System;
using System.Collections.Generic;
using System.Net;
using System.Text;

namespace socks5.Socks5Client
{
    public class Socks
    {
        public static AuthTypes Greet(Client client)
        {
            client.Send(new byte[] { 0x05, Convert.ToByte(3), (byte)AuthTypes.None, (byte)AuthTypes.Login, (byte)AuthTypes.SocksEncrypt });//(byte)AuthTypes.SocksBoth });//(byte)AuthTypes.SocksCompress
            byte[] buffer = new byte[512];
            int received = client.Receive(buffer, 0, buffer.Length);
            if(received > 0)
            {
                //check for server version.
                if (buffer[0] == 0x05)
                {
                    return (AuthTypes)buffer[1];
                }
            }
            return 0;
        }

        public static int SendLogin(Client cli, string Username, string Password)
        {
            byte[] x = new byte[Username.Length + Password.Length + 3];
            int total = 0;
            x[total++] = 0x01;
            x[total++] = Convert.ToByte(Username.Length);
            Buffer.BlockCopy(Encoding.ASCII.GetBytes(Username), 0, x, 2, Username.Length);
            total += Username.Length;
            x[total++] = Convert.ToByte(Password.Length); 
            Buffer.BlockCopy(Encoding.ASCII.GetBytes(Password), 0, x, total, Password.Length);
            //send request.
            cli.Send(x);
            byte[] buffer = new byte[512];
            cli.Receive(buffer, 0, buffer.Length);
            if (buffer[1] == 0x00)
            {
                return 1;
            }
            else if (buffer[1] == 0xFF)
            {
                return 0;
            }
            return 0;
        }

        public static socks5.Socks.SocksError SendRequest(Client cli, SocksEncryption enc, string ipOrDomain, int port)
        {
            AddressType type;
            IPAddress ipAddress;
            if (!IPAddress.TryParse(ipOrDomain, out ipAddress))
                //it's a domain. :D (hopefully).
                type = AddressType.Domain;
            else
                type = AddressType.IP;
            SocksRequest sr = new SocksRequest(StreamTypes.Stream, type, ipOrDomain, port);
            //send data.
            byte[] p = sr.GetData(false);
            p[1] = 0x01;
            //process data.
            cli.Send(enc.ProcessOutputData(p, 0, p.Length));
            byte[] buffer = new byte[512];
            //process input data.
            int recv = cli.Receive(buffer, 0, buffer.Length);
            if(recv == -1)
            {
                return SocksError.Failure;
            }
            byte[] buff = enc.ProcessInputData(buffer, 0, recv);
            
            return (SocksError)buff[1];
        }

        public static bool DoSocksAuth(Socks5Client p, string Username, string Password)
        {
            AuthTypes auth = Socks.Greet(p.Client);
            if (auth == AuthTypes.Unsupported)
            {
                p.Client.Disconnect();
                return false;
            }
            if (auth != AuthTypes.None)
            {
                p.enc = new Encryption.SocksEncryption();
                switch (auth)
                {
                    case AuthTypes.Login:
                        //logged in.
                        p.enc.SetType(AuthTypes.Login);
                        //just reqeust login?

                        break;
                    case AuthTypes.SocksBoth:
                        //socksboth.
                        p.enc.SetType(AuthTypes.SocksBoth);
                        p.enc.GenerateKeys();
                        //send public key.
                        p.Client.Send(p.enc.GetPublicKey());
                        //now receive key.

                        byte[] buffer = new byte[4096];
                        int keysize = p.Client.Receive(buffer, 0, buffer.Length);
                        p.enc.SetKey(buffer, 0, keysize);
                        //let them know we got it
                        //now receive our encryption key.
                        int enckeysize = p.Client.Receive(buffer, 0, buffer.Length);
                        //decrypt with our public key.
                        byte[] newkey = new byte[enckeysize];
                        Buffer.BlockCopy(buffer, 0, newkey, 0, enckeysize);
                        p.enc.SetEncKey(p.enc.key.Decrypt(newkey, false));
                        //now we share our encryption key.
                        p.Client.Send(p.enc.ShareEncryptionKey());

                        break;
                    case AuthTypes.SocksEncrypt:
                        p.enc.SetType(AuthTypes.SocksEncrypt);
                        p.enc.GenerateKeys();
                        //send public key.
                        p.Client.Send(p.enc.GetPublicKey());
                        //now receive key.

                        buffer = new byte[4096];
                        keysize = p.Client.Receive(buffer, 0, buffer.Length);
                        p.enc.SetKey(buffer, 0, keysize);
                        //now receive our encryption key.
                        enckeysize = p.Client.Receive(buffer, 0, buffer.Length);
                        //decrypt with our public key.
                        newkey = new byte[enckeysize];
                        Buffer.BlockCopy(buffer, 0, newkey, 0, enckeysize);
                        p.enc.SetEncKey(p.enc.key.Decrypt(newkey, false));
                        //now we share our encryption key.

                        p.Client.Send(p.enc.ShareEncryptionKey());

                        //socksencrypt.
                        break;
                    case AuthTypes.SocksCompress:
                        p.enc.SetType(AuthTypes.SocksCompress);
                        //sockscompress.
                        break;
                    default:
                        p.Client.Disconnect();
                        return false;
                }
                if (p.enc.GetAuthType() != AuthTypes.Login)
                {
                    //now receive login params.
                    byte[] buff = new byte[1024];
                    int recv = p.Client.Receive(buff, 0, buff.Length);
                    //check for 
                    if (recv > 0)
                    {
                        //check if socks5 version is 5
                        if (buff[0] == 0x05)
                        {
                            //good.
                            if (buff[1] == (byte)AuthTypes.Login)
                            {
                                if (Username == null || Password == null) { p.Client.Sock.Close(); return false; }
                                int ret = Socks.SendLogin(p.Client, Username, Password);
                                if (ret != 1)
                                {
                                    p.Client.Sock.Close();
                                    return false;
                                }
                            }
                            else
                            {
                                //idk? close for now.
                                p.Client.Disconnect();
                                return false;
                            }
                        }
                    }
                    else
                    {
                        p.Client.Disconnect();
                        return false;
                    }
                }
                else
                {
                    if (Username == null || Password == null) { p.Client.Sock.Close(); return false; }
                    int ret = Socks.SendLogin(p.Client,Username, Password);
                    if (ret != 1)
                    {
                        p.Client.Sock.Close();
                        return false;
                    }
                }
            }
            return true;
        }
    }
}