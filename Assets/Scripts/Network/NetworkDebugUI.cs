using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Unity.Netcode;
using StrategyCore;

namespace StrategyCore
{
    public class NetworkDebugUI : MonoBehaviour
    {
        [SerializeField] private Button hostBtn;
        [SerializeField] private Button serverBtn;
        [SerializeField] private Button clientBtn;

        private void Awake()
        {
            hostBtn.onClick.AddListener(() =>
            {
                NetworkConnectionHandler.instance.StartHost("test player");
            });
            serverBtn.onClick.AddListener(() =>
            {
                NetworkManager.Singleton.StartServer();
            });
            clientBtn.onClick.AddListener(() =>
            {
                NetworkConnectionHandler.instance.StartClient("test player");
            });
        }

        // Start is called before the first frame update
        void Start()
        {

        }

        // Update is called once per frame
        void Update()
        {

        }
    }
}
