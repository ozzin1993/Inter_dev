using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace StrategyCore
{
    // Attribute wrapper is a simple wrapper for holding Resource:Amount pair

    [System.Serializable]
    public class ResourceWrapper
    {
        public Resource type;
        public int value;

        public ResourceWrapper() { }

        public ResourceWrapper(Resource type, int value)
        {
            this.type = type;
            this.value = value;
        }

        public ResourceWrapper(ResourceWrapper rw)
        {
            this.type = rw.type;
            this.value = rw.value;
        }

        public ResourceWrapper(ResourceWrapperID rw)
        {
            this.type = GameResources.instance.gameResources[rw.typeID].type;
            this.value = rw.value;
        }

        /// <summary>
        /// Returns the resource wrapper with ids instead of types. Used in SaveManager.
        /// </summary>
        public static ResourceWrapperID[] IDArray(ResourceWrapper[] rw)
        {
            ResourceWrapperID[] idArray = new ResourceWrapperID[rw.Length];

            for (int i = 0; i < rw.Length; i++)
            {
                idArray[i] = new ResourceWrapperID(rw[i]);
            }

            return idArray;
        }

        /// <summary>
        /// Converts resouce ID into resource Types. Used in SaveManager.
        /// </summary>
        public static ResourceWrapper[] TypeArray(ResourceWrapperID[] rw)
        {
            ResourceWrapper[] typeArray = new ResourceWrapper[rw.Length];

            for (int i = 0; i < rw.Length; i++)
            {
                typeArray[i] = new ResourceWrapper(rw[i]);
            }

            return typeArray;
        }
    }

    // This is used to assign unique animation blending index based on the resource collected
    [System.Serializable]
    public class ResourceAnimationBlendingIndex
    {
        public Resource type;
        public float blendingIndex;
    }

    // Used for save manager, converts the resource type into id
    [System.Serializable]
    public class ResourceWrapperID
    {
        public int typeID;
        public int value;

        public ResourceWrapperID(ResourceWrapper rw)
        {
            this.typeID = GameResources.instance.GetResourceID(rw);
            this.value = rw.value;
        }
    }
}
