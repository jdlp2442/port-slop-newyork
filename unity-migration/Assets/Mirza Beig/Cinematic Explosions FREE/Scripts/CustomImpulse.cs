using System.Collections;
using System.Collections.Generic;
using UnityEngine;



namespace MirzaBeig.CinematicExplosionsFree
{
    public class CustomImpulse : MonoBehaviour
    {
        Unity.Cinemachine.CinemachineImpulseSource source;

        void Start()
        {

        }

        void OnEnable()
        {
            if (!source)
            {
                source = GetComponent<Unity.Cinemachine.CinemachineImpulseSource>();
            }

            source.GenerateImpulse();
        }

        void Update()
        {

        }
    }
}
