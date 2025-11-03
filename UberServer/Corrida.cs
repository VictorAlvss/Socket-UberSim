using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UberServer
{
    internal class Corrida
    {

        public int Id { get; set; }
        public string Origem { get; set; }
        public string Destino { get; set; }
        public string PassageiroId { get; set; }
    }
}
