using System.Collections.Generic;
using Pulllist.DAL.CostRollup;

namespace Pulllist.Service
{
    public class CostRollupService : BaseService<CostRollupDAO, CostRollupRow>
    {
        public List<CostRollupRow> Search(string customer, string status)
        {
            DAO.SetConnection(base.GetConnection("MATERIAL", "MATERIAL"));
            return DAO.Search(customer, status);
        }

        public CostRollupMutationResult Add(CostRollupWriteRequest request, string actor)
        {
            DAO.SetConnection(base.GetConnection("MATERIAL", "MATERIAL"));
            return DAO.Add(request, actor);
        }

        public CostRollupMutationResult Update(CostRollupWriteRequest request, string actor)
        {
            DAO.SetConnection(base.GetConnection("MATERIAL", "MATERIAL"));
            return DAO.Update(request, actor);
        }

        public CostRollupMutationResult Delete(CostRollupSnapshot original)
        {
            DAO.SetConnection(base.GetConnection("MATERIAL", "MATERIAL"));
            return DAO.Delete(original);
        }
    }
}
