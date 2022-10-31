using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace StoryGenerator
{
    public class ViewWhiskers : MonoBehaviour
    {

        private string _nameCollision;
        private string _nameClsParent;
        private string _nameClsGrdParent;
        private Vector3 _position;

        private string _crntNameCollision;
        private string _crntNameClsParent;
        private string _crentNameGrdParent;

        public bool _isDitect;

        public string NameCollision{ get{ return _nameCollision;}}
        public string NameClsParent{ get{return _nameClsParent;}}
        public string NameClsGrdParent{ get{return _nameClsGrdParent;}}
        public Vector3 POsition{ get{ return _position;}}

    // Start is called before the first frame update
        void Start()
        {
            _isDitect = false;
        }

        void OnTriggerEnter(Collider cld)
        {
            _isDitect = true;

            _nameCollision = cld.gameObject.name;
            
            if(cld.transform.parent != null)
            {
                _nameClsParent = cld.transform.parent.name;
            }

            if(cld.transform.parent.parent != null)
            {
                _nameClsGrdParent = cld.transform.parent.parent.name;
            }

            _position = cld.gameObject.transform.position;
        }
        // Update is called once per frame
        /*
        void Update()
        {
        
    
        }
        */
    }
}

