﻿using BitcoinLib.Requests.CreateRawTransaction;
using BitcoinLib.Requests.SignRawTransaction;
using BitcoinLib.Responses;
using BitcoinLib.Services.Coins.Mogwaicoin;
using log4net;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Reflection;
using WoMInterface.Game.Enums;
using WoMInterface.Game.Interaction;
using WoMInterface.Game.Model;
using WoMInterface.Tool;

namespace WoMInterface.Node
{
    public class Blockchain
    {
        private static readonly ILog _log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private const decimal mogwaiCost = 1.0m;

        private const decimal txFee = 0.0001m;

        IMogwaicoinService mogwaiService;

        private static Blockchain instance;

        public static Blockchain Instance => instance ?? (instance = new Blockchain());

        private CachingService cachingService = new CachingService();
        public CachingService CachingService => cachingService;

        public Blockchain(string daemonUrl, string rpcUsername, string rpcPassword, string walletPassword)
        {
            mogwaiService = new MogwaicoinService(daemonUrl, rpcUsername, rpcPassword, walletPassword, 10);
        }

        private Blockchain()
        {
            mogwaiService = new MogwaicoinService(
                ConfigurationManager.AppSettings["daemonUrl"],
                ConfigurationManager.AppSettings["rpcUsername"],
                ConfigurationManager.AppSettings["rpcPassword"],
                ConfigurationManager.AppSettings["walletPassword"],
                10);
        }

        /// <summary>
        /// 
        /// </summary>
        public void Exit()
        {
            Console.WriteLine("Persisting current cached informations.");
            cachingService.Persist(true, true);
            Console.WriteLine("Stoping rpc service.");
            mogwaiService.Stop();
        }

        /// <summary>
        /// 
        /// </summary>
        public void Cache(bool clear, bool initial)
        {
            var progress = new ProgressBar(60);

            uint blockcount = mogwaiService.GetBlockCount();

            int maxBlockCached = clear ? 0 : cachingService.BlockHashCache.MaxBlockHash;

            for (int i = maxBlockCached + 1; i < blockcount; i++)
            {
                if (initial)
                {
                    progress.Update(i * 100 / blockcount);
                }

                string blockHash = mogwaiService.GetBlockHash(i);
                cachingService.BlockHashCache.BlockHashDict.Add(i, blockHash);
            }

            if (initial)
            {
                progress.Update(100);
                cachingService.Persist(true, false);
                Console.WriteLine();
            }
        }

        /// <summary>
        /// 
        /// </summary>
        public void CacheStats()
        {
            Console.WriteLine($"Blockhashes: {cachingService.BlockHashCache.BlockHashDict.Count()}");
            Console.WriteLine($"BlockHeight: {cachingService.BlockHashCache.MaxBlockHash} [curr:{mogwaiService.GetBlockCount()}]");
            Console.WriteLine($"MogwaiPointers: {cachingService.MogwaisCache.MogwaiPointers.Count()}");
        }
        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public string GetWalletVersion()
        {
            return mogwaiService.GetInfo().WalletVersion;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="address"></param>
        /// <param name="evolveShifts"></param>
        /// <param name="mogwai"></param>
        /// <returns></returns>
        public BoundState TryGetMogwai(string address, out Mogwai mogwai)
        {
            mogwai = null;

            if (!TryGetMogwaiAddress(address, out string mogwaiAddress))
            {
                return BoundState.NONE;
            }

            BoundState boundState = IsMogwaiBound(address, mogwaiAddress, out Dictionary<double, Shift> shifts);

            if (boundState == BoundState.BOUND)
            {
                mogwai = new Mogwai(address, shifts);
            }

            return boundState;
        }

        /// <summary>
        /// TODO: This is a workaround function please remove once fixed.
        /// </summary>
        /// <param name="fromBlockHeight"></param>
        /// <param name="pattern"></param>
        /// <param name="blockHashes"></param>
        /// <returns></returns>
        public bool TryGetBlockHashes(int fromBlockHeight, int toBlockHeight, string[] pattern, out Dictionary<int, string> blockHashes)
        {
            blockHashes = new Dictionary<int, string>();

            uint blockcount = mogwaiService.GetBlockCount();

            if (cachingService.BlockHashCache.MaxBlockHash < blockcount)
            {
                Cache(false, false);
            }

            string blockHash;
            for (int i = fromBlockHeight; i < toBlockHeight; i++)
            {
                if (!cachingService.BlockHashCache.BlockHashDict.TryGetValue(i, out blockHash))
                {
                    return false;
                }
                if (pattern == null || StringHelpers.StringContainsStringFromArray(blockHash, pattern))
                {
                    blockHashes.Add(i, blockHash);
                }
            }

            return true;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="mogwaiAddress"></param>
        /// <returns></returns>
        private Dictionary<double, Shift> GetShifts(string mogwaiAddress, out bool openShifts)
        {
            var result = new Dictionary<double, Shift>();

            var allTxs = new List<ListTransactionsResponse>();

            List<ListTransactionsResponse> listTxs = null;
            int index = 0;
            int chunkSize = 50;
            while (listTxs == null || listTxs.Count > 0)
            {
                listTxs = mogwaiService.ListTransactions("", chunkSize, index);
                allTxs.AddRange(listTxs);
                index += chunkSize;
            }

            var incUnconfTx = allTxs.Where(p => p.Address == mogwaiAddress && p.Category == "send").OrderBy(p => p.Time).ThenBy(p => p.BlockIndex);
            var validTx = incUnconfTx.Where(p => p.Confirmations > 0);
            openShifts = incUnconfTx.Count() > validTx.Count();

            var pubMogAddressHex = HexHashUtil.ByteArrayToString(Base58Encoding.Decode(mogwaiAddress));

            bool creation = false;
            int lastBlockHeight = 0;
            foreach (var tx in validTx)
            {
                decimal amount = Math.Abs(tx.Amount);
                if (!creation && amount < mogwaiCost)
                    continue;

                creation = true;

                var block = mogwaiService.GetBlock(tx.BlockHash);

                if (lastBlockHeight != 0 && lastBlockHeight + 1 < block.Height)
                {
                    // add small shifts
                    if (TryGetBlockHashes(lastBlockHeight + 1, block.Height, null, out Dictionary<int, string> blockHashes))
                    {
                        foreach (var blockHash in blockHashes)
                        {
                            result.Add(blockHash.Key, new Shift(result.Count(), pubMogAddressHex, blockHash.Key, blockHash.Value));
                        }
                    }
                }

                lastBlockHeight = block.Height;

                result.Add(block.Height, new Shift(result.Count(), tx.Time, pubMogAddressHex, block.Height, tx.BlockHash, tx.BlockIndex, tx.TxId, amount, Math.Abs(tx.Fee + txFee)));
            }

            // add small shifts
            if (creation && TryGetBlockHashes(lastBlockHeight + 1, (int)mogwaiService.GetBlockCount(), null, out Dictionary<int, string> finalBlockHashes))
            {
                foreach (var blockHash in finalBlockHashes)
                {
                    result.Add(blockHash.Key, new Shift(result.Count(), pubMogAddressHex, blockHash.Key, blockHash.Value));
                }
            }

            //result.ForEach(p => Console.WriteLine(p.ToString()));
            return result;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="address"></param>
        /// <returns></returns>
        public bool BindMogwai(string address)
        {
            if (!TryGetMogwaiAddress(address, out string mogwaiAddress))
            {
                return false;
            }

            //Console.WriteLine($"{address} --> {mogwaiAddress}");

            if (IsMogwaiBound(address, mogwaiAddress, out Dictionary<double, Shift> shifts) != BoundState.NONE)
            {
                Console.WriteLine("Mogwai already exists or is in creation process!");
                return false;
            }

            var burned = BurnMogs(address, mogwaiAddress, mogwaiCost, txFee);

            return burned;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="interaction"></param>
        /// <returns></returns>
        public bool SendInteraction(string address, Interaction interaction)
        {
            if (!TryGetMogwaiAddress(address, out string mogwaiAddress))
            {
                return false;
            }

            if (IsMogwaiBound(address, mogwaiAddress, out Dictionary<double, Shift> shifts) != BoundState.BOUND)
            {
                return false;
            }

            var burned = BurnMogs(address, mogwaiAddress, interaction.GetValue1(), txFee + interaction.GetValue2());

            return burned;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="account"></param>
        /// <param name="addresses"></param>
        /// <returns></returns>
        public Dictionary<string, decimal[]> UnconfirmedTxOnAddresses(string account, string[] addresses)
        {
            var allTxs = new List<ListTransactionsResponse>();

            List<ListTransactionsResponse> listTxs = null;
            int index = 0;
            int chunkSize = 50;
            while (listTxs == null || listTxs.Count > 0)
            {
                listTxs = mogwaiService.ListTransactions(account, chunkSize, index);
                allTxs.AddRange(listTxs);
                index += chunkSize;
            }
            Dictionary<string, decimal[]> unconfirmedFunds = new Dictionary<string, decimal[]>();
            foreach (var tx in allTxs)
            {
                if (addresses.Contains(tx.Address) && tx.Category == "receive" && tx.Confirmations < 6)
                {
                    if (unconfirmedFunds.TryGetValue(tx.Address, out decimal[] funds))
                    {
                        if (tx.Confirmations == 0)
                        {
                            funds[0] = funds[0] + tx.Amount;
                        }
                        else
                        {
                            funds[1] = funds[1] + tx.Amount;
                        }
                    }
                    else
                    {
                        if (tx.Confirmations == 0)
                        {
                            funds = new decimal[] { tx.Amount, 0m };
                        }
                        else
                        {
                            funds = new decimal[] { 0m, tx.Amount };
                        }
                    }
                    unconfirmedFunds[tx.Address] = funds;
                }
            }

            return unconfirmedFunds;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="fromaddress"></param>
        /// <param name="listUnspent"></param>
        /// <returns></returns>
        public decimal UnspendFunds(string fromaddress, int minConf, out List<ListUnspentResponse> listUnspent)
        {
            listUnspent = mogwaiService.ListUnspent(minConf, 9999999, new List<string> { fromaddress });
            var unspentAmount = listUnspent.Sum(p => p.Amount);
            return unspentAmount;
        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="fromaddress"></param>
        /// <returns></returns>
        public decimal UnspendFunds(string fromaddress, int minConf = 6)
        {
            return UnspendFunds(fromaddress, minConf, out List<ListUnspentResponse> listUnspent);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="fromaddresses"></param>
        /// <param name="minConf"></param>
        /// <returns></returns>
        public Dictionary<string, decimal> UnspendFunds(List<string> fromaddresses, int minConf)
        {
            var listUnspent = mogwaiService.ListUnspent(minConf, 9999999, fromaddresses);
            var dict = listUnspent.GroupBy(y => y.Address).ToDictionary(g => g.Key, g => g.Sum(v => v.Amount));
            return dict;
        }

        /// <summary>
        /// Function to burn mogs
        /// </summary>
        /// <param name="fromaddress"></param>
        /// <param name="toaddress"></param>
        /// <param name="burnMogs"></param>
        /// <param name="txFee"></param>
        /// <returns></returns>
        public bool BurnMogs(string fromaddress, string toaddress, decimal burnMogs, decimal txFee)
        {
            var unspentAmount = UnspendFunds(fromaddress, 6, out List<ListUnspentResponse> listUnspent);
            if (unspentAmount < (burnMogs + txFee))
            {
                Console.WriteLine($"Address hasn't enough funds {unspentAmount} to burn that amount of mogs {(burnMogs + txFee)}!");
                return false;
            }

            // create raw transaction
            var rawTxRequest = new CreateRawTransactionRequest();

            // adding all unspent txs
            listUnspent.ForEach(p =>
            {
                rawTxRequest.AddInput(new CreateRawTransactionInput() { TxId = p.TxId, Vout = p.Vout });
            });

            rawTxRequest.AddOutput(new CreateRawTransactionOutput() { Address = toaddress, Amount = burnMogs });

            // check if we need a changeaddress
            if ((unspentAmount - burnMogs) > txFee)
            {
                //Console.WriteLine($"Adding change output {unspentAmount - burnMogs - txFee}");
                rawTxRequest.AddOutput(new CreateRawTransactionOutput() { Address = fromaddress, Amount = unspentAmount - burnMogs - txFee });
            }

            var rawTx = mogwaiService.CreateRawTransaction(rawTxRequest);
            _log.Info($"rawTx: {rawTx}");

            var signedRawTx = mogwaiService.SignRawTransaction(new SignRawTransactionRequest(rawTx));
            _log.Info($"signedRawTx: {signedRawTx.Hex}");

            var sendRawTx = mogwaiService.SendRawTransaction(signedRawTx.Hex, false);
            _log.Info($"sendRawTx: {sendRawTx}");
            Console.WriteLine($"txid: {sendRawTx}");

            return true;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="mogwaiAddress"></param>
        /// <param name="shifts"></param>
        /// <returns></returns>
        internal BoundState IsMogwaiBound(string address, string mogwaiAddress, out Dictionary<double, Shift> shifts)
        {
            shifts = GetShifts(mogwaiAddress, out bool openShifts);
            // no shifts found
            if (shifts.Count == 0)
            {
                //Console.WriteLine("No mogwai bound to this address, 0 transactions found, try to bind a mogwai first!");
                return openShifts ? BoundState.WAIT : BoundState.NONE;
            }
            return BoundState.BOUND;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="address"></param>
        /// <param name="mogwaiAddress"></param>
        /// <returns></returns>
        private bool TryGetMogwaiAddress(string address, out string mogwaiAddress)
        {
            var mirrorAddress = mogwaiService.MirrorAddress(address);
            if (!mirrorAddress.IsMine || !mirrorAddress.IsValid || !mirrorAddress.IsMirAddrValid)
            {
                //Console.WriteLine($"Haven't found a valid mogwaiwaddress for {address}!");
                mogwaiAddress = null;
                return false;
            }

            mogwaiAddress = mirrorAddress.MirAddress;
            //Console.WriteLine($"mogwaiaddress: {mogwaiAddress}");
            return true;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public string GetDepositAddress()
        {
            var depositAddress = mogwaiService.GetAddressesByAccount("Deposit").FirstOrDefault();
            if (depositAddress == null)
            {
                depositAddress = mogwaiService.GetAccountAddress("Deposit");
            }
            return depositAddress;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="mogwaiAddress"></param>
        /// <param name="tryes"></param>
        /// <returns></returns>
        public bool NewMogwaiAddress(out string mogwaiAddress, int tryes = 10)
        {
            mogwaiAddress = null;
            List<string> listPoolAddresses;

            for (int i = 0; i < tryes; i++)
            {
                listPoolAddresses = mogwaiService.GetAddressesByAccount("Pool");

                if (listPoolAddresses.Count == 0)
                {
                    mogwaiService.GetNewAddress("Pool");
                }

                // get new created address
                listPoolAddresses = mogwaiService.GetAddressesByAccount("Pool");

                if (TryGetMogwaiAddress(listPoolAddresses[0], out mogwaiAddress))
                {
                    mogwaiService.SetAccount(listPoolAddresses[0], "Mogwai");
                    return true;
                }
                else
                {
                    mogwaiService.SetAccount(listPoolAddresses[0], "");
                }
            }

            return false;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public Dictionary<string, string> ValidMogwaiAddresses()
        {
            var listAddresses = mogwaiService.GetAddressesByAccount("Mogwai");
            Dictionary<string, string> result = new Dictionary<string, string>();
            listAddresses.ForEach(p =>
            {
                if (TryGetMogwaiAddress(p, out string mogwaiAddress))
                {
                    result.Add(p, mogwaiAddress);
                }
                else
                {
                    mogwaiService.SetAccount(p, "");
                }
            });
            return result;
        }
    }
}
