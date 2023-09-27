using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Web.Http;
using System.Web.Http.Results;
using Captura.Api.Models;
using Newtonsoft.Json;
using NITGEN.SDK.NBioBSP;
using static NITGEN.SDK.NBioBSP.NBioAPI;

namespace Captura.Api.Controllers
{

    [RoutePrefix("api/public/v1/captura")]
    public class CapturaController : ApiController
    {
        [HttpGet]
        [Route("Capturar/{id:int:min(1)}")]
        public string Capturar(int id)
        {
            //Instanciando e ajustando
            NBioAPI nBioAPI = new NBioAPI();
            NBioAPI.Type.INIT_INFO_0 initInfo0;
            uint ret = nBioAPI.GetInitInfo(out initInfo0);
            if (ret == NBioAPI.Error.NONE) {
                initInfo0.EnrollImageQuality = Convert.ToUInt32(50);
                initInfo0.VerifyImageQuality = Convert.ToUInt32(30);
                initInfo0.DefaultTimeout = Convert.ToUInt32(10000);
                initInfo0.SecurityLevel = (int)NBioAPI.Type.FIR_SECURITY_LEVEL.NORMAL - 1;
            }

            // NBioAPI.IndexSearch m_IndexSearch = new NBioAPI.IndexSearch(nBioAPI);

            //Realiza a captura
            NBioAPI.Type.HFIR capturaHFIR;
            nBioAPI.OpenDevice(NBioAPI.Type.DEVICE_ID.AUTO);
            nBioAPI.Capture(out capturaHFIR);
            nBioAPI.CloseDevice(NBioAPI.Type.DEVICE_ID.AUTO);
            if (capturaHFIR == null) return null;

            //Converte para texto
            NBioAPI.Type.FIR_TEXTENCODE texto;
            nBioAPI.GetTextFIRFromHandle(capturaHFIR, out texto, true);
            return texto.TextFIR;
        }

        [HttpGet]
        [Route("Enroll/{id:int:min(1)}")]
        public string Enroll(int id)
        {
            //Instanciando
            NBioAPI nBioAPI = new NBioAPI();
            NBioAPI.IndexSearch m_IndexSearch = new NBioAPI.IndexSearch(nBioAPI);

            // Sem mensagem de boas vindas
            NBioAPI.Type.WINDOW_OPTION m_WinOption = new NBioAPI.Type.WINDOW_OPTION();
            m_WinOption.WindowStyle = (uint) NBioAPI.Type.WINDOW_STYLE.NO_WELCOME;

            //Realiza a captura
            NBioAPI.Type.HFIR capturaHFIR;
            nBioAPI.OpenDevice(NBioAPI.Type.DEVICE_ID.AUTO);
            nBioAPI.Enroll(null, out capturaHFIR, null, NBioAPI.Type.TIMEOUT.DEFAULT, null, m_WinOption); //nBioAPI.Enroll(out capturaHFIR, null);
            nBioAPI.CloseDevice(NBioAPI.Type.DEVICE_ID.AUTO);
            if (capturaHFIR == null) return null;

            //Converte para texto
            NBioAPI.Type.FIR_TEXTENCODE texto;
            nBioAPI.GetTextFIRFromHandle(capturaHFIR, out texto, true);
            return texto.TextFIR;
        }

        [HttpGet]
        [Route("Comparar")]
        public bool Comparar(string digital)
        {
            //Converte a digital recebida
            NBioAPI.Type.FIR_TEXTENCODE textoFIR = new NBioAPI.Type.FIR_TEXTENCODE();
            textoFIR.TextFIR = digital.ToString();

            //Realiza a captura
            NBioAPI nBioAPI = new NBioAPI();
            NBioAPI.Type.HFIR capturaHFIR;
            nBioAPI.OpenDevice(NBioAPI.Type.DEVICE_ID.AUTO);
            nBioAPI.Capture(out capturaHFIR);
            nBioAPI.CloseDevice(NBioAPI.Type.DEVICE_ID.AUTO);

            //Realiza a comparacao
            bool resultado;
            nBioAPI.VerifyMatch(capturaHFIR, textoFIR, out resultado, new NBioAPI.Type.FIR_PAYLOAD());

            return resultado;
        }

        [HttpPost]
        [Route("Identificar")]
        public int? Identificar(List<Usuario> usuarios)
        {
            //Instanciando
            NBioAPI nBioAPI = new NBioAPI();
            NBioAPI.IndexSearch indexSearch = new NBioAPI.IndexSearch(nBioAPI);
            NBioAPI.IndexSearch.FP_INFO[] fpInfoArray;
            uint s; //Usado para checar se havera sucesso nos proximos comandos

            //Alimenta a memória com os cadastros recebidos
            s = indexSearch.InitEngine();
            uint dataCount;
            indexSearch.GetDataCount(out dataCount);
            System.Diagnostics.Debug.WriteLine("dataCount" + dataCount);

            usuarios.ForEach(x => {
                //Converte a digital recebida
                NBioAPI.Type.FIR_TEXTENCODE textoFIR = new NBioAPI.Type.FIR_TEXTENCODE();
                textoFIR.TextFIR = x.digital;
                s = indexSearch.AddFIR(textoFIR, x.id, out fpInfoArray);
                if (s != NBioAPI.Error.NONE) throw new Exception("Ocorreu uma falha ao tentar carregar uma das digitais da base de dados. Usuario cod.: " + x.id + ". Erro n" + s + "|" + NBioAPI.Error.GetErrorDescription(s));
            });

            //Realiza a captura
            NBioAPI.Type.HFIR capturaHFIR;
            s = nBioAPI.OpenDevice(NBioAPI.Type.DEVICE_ID.AUTO);
            if (s != NBioAPI.Error.NONE) throw new Exception("Não foi possível inicializar o leitor de digital. Erro n" + s);

            s = nBioAPI.Capture(out capturaHFIR);
            if (s != NBioAPI.Error.NONE) {
                if (s == NBioAPI.Error.USER_CANCEL) return null; //O usuario cancelou a leitura
                throw new Exception("Ocorreu uma falha na leitura da digital. Erro n" + s);
            }

            s = nBioAPI.CloseDevice(NBioAPI.Type.DEVICE_ID.AUTO);
            if (s != NBioAPI.Error.NONE) throw new Exception("Não foi possível auto-desligar o leitor de digital após escanear. Erro n" + s);
            if (capturaHFIR == null) return null;

            //Ajustes
            NBioAPI.IndexSearch.FP_INFO fpInfo;
            NBioAPI.IndexSearch.CALLBACK_INFO_0 cbInfo0 = new NBioAPI.IndexSearch.CALLBACK_INFO_0();
            cbInfo0.CallBackFunction = new NBioAPI.IndexSearch.INDEXSEARCH_CALLBACK(myCallback);

            //Realizando a busca
            s = indexSearch.IdentifyData(capturaHFIR, NBioAPI.Type.FIR_SECURITY_LEVEL.HIGH, out fpInfo, cbInfo0);
            indexSearch.ClearDB();
            indexSearch.TerminateEngine();
            if (s != NBioAPI.Error.NONE)
            {
                if (s == NBioAPI.Error.INDEXSEARCH_IDENTIFY_FAIL) return 0; //Nao encontrou
                else throw new Exception(NBioAPI.Error.GetErrorDescription(s));
            }
            return (int) fpInfo.ID; //Sucesso, retorna o ID
        }

        public uint myCallback(ref NBioAPI.IndexSearch.CALLBACK_PARAM_0 cbParam0, IntPtr userParam)
        {
            return NBioAPI.IndexSearch.CALLBACK_RETURN.OK;
        }

    }
}
