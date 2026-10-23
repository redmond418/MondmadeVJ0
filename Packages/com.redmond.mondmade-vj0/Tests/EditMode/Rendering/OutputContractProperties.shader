Shader "Hidden/MondmadeVJ0/OutputContractProperties"
{
    Properties
    {
        _ContractFloat ("Float", Float) = 0
        _ContractInt ("Integer", Integer) = 0
        _ContractVector ("Vector", Vector) = (0, 0, 0, 0)
        _ContractColor ("Color", Color) = (1, 1, 1, 1)
        _ContractTexture ("Texture", 2D) = "white" {}
    }
    SubShader
    {
        Pass
        {
            CGPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "UnityCG.cginc"

            struct AppData { float4 vertex : POSITION; };
            struct Varyings { float4 position : SV_POSITION; };
            float4 _ContractColor;

            Varyings Vert(AppData input)
            {
                Varyings output;
                output.position = UnityObjectToClipPos(input.vertex);
                return output;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                return _ContractColor;
            }
            ENDCG
        }
    }
}
