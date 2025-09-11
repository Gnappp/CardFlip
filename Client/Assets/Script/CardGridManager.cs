using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using System.Threading.Tasks;

public class CardGridManager : MonoBehaviour
{
    public GridLayoutGroup cardGrid;        
    public List<GameObject> cardSlots;  
    public Vector2 fixedCell = new Vector2(96, 128);
    public System.Action<int> OnFlipRequest;

    int _rows, _cols;
    bool[] _revealed;
    int _first;

    public void SetupBoard(int cols, int rows)
    {
        _rows = rows;
        _cols = cols;
        _revealed = new bool[rows * cols];
        _first = -1;

        cardGrid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        cardGrid.constraintCount = cols;
        cardGrid.cellSize = fixedCell;

        int total = rows * cols;

        for (int i = 0; i < cardSlots.Count; i++)
        {
            bool use = i < total ;
            var go = cardSlots[i];

            go.SetActive(i < total);
            if (!go.activeSelf) continue;

            var btn = go.GetComponent<Button>();
            var img = go.GetComponent<Image>();

            if (btn) 
                btn.interactable = use;      
            if (img) 
                img.enabled = use;        

            int captured = i; 
            if (btn)
            {
                btn.onClick.RemoveAllListeners();
                btn.onClick.AddListener(() => OnClickCard(captured));
            }
        }
    }

    void OnClickCard(int poolIndex)
    {
        OnFlipRequest?.Invoke(poolIndex);
        Debug.Log($"FLIP idx={poolIndex}");
    }

    public async Task ResetCardGrid()
    {
        try
        {
            await Task.Delay(1000);
            for (int i = 0; i < _rows * _cols; i++)
            {
                var t = cardSlots[i].GetComponentInChildren<Text>();
                t.text = "";
            }
        }
        catch
        {
            return ;
        }
        return ;
    }

    public async Task<bool> FirstShowFlip(List<int> cards, int dur, int all_dur)
    {
        try
        {
            for (int i = 0; i < cards.Count; i++)
            {
                var t = cardSlots[i].GetComponentInChildren<Text>();
                t.text = cards[i].ToString();
                await Task.Delay(dur);
                t.text = "";
            }
            for (int i = 0; i < cards.Count; i++)
            {
                var t = cardSlots[i].GetComponentInChildren<Text>();
                t.text = cards[i].ToString();
            }

            await Task.Delay(all_dur);
            for (int i = 0; i < cards.Count; i++)
            {
                var t = cardSlots[i].GetComponentInChildren<Text>();
                t.text = "";
            }
        }
        catch
        {
            return false;
        }
        return true;
    }
    public void ShowFirstFlip(int idx,int card)
    {
        _revealed[idx] = true; 
        var t= cardSlots[idx].GetComponentInChildren<Text>();
        cardSlots[idx].GetComponent<Button>().interactable = false;
        t.text = card.ToString();
        _first = idx;
    }

    public int GetFirstIndex()
    {
        return _first;
    }

    public async Task<bool> ShowSecondFlipFail(int idx,int card,int dur)
    {
        try
        {
            var t = cardSlots[idx].GetComponentInChildren<Text>();
            cardSlots[idx].GetComponent<Button>().interactable = false;
            t.text = card.ToString();
            await Task.Delay(dur);
            t.text = "";
            cardSlots[idx].GetComponent<Button>().interactable = true;

            t = cardSlots[_first].GetComponentInChildren<Text>();
            t.text = "";

            cardSlots[_first].GetComponent<Button>().interactable = true;
            _first = -1;
            _revealed[_first] = false;
        }
        catch
        {
            return false;
        }
        return true;
    }

    public void ShowSecondFlipComplete(int idx, int card)
    {
        var t = cardSlots[idx].GetComponentInChildren<Text>();
        t.text = card.ToString();

        cardSlots[idx].GetComponent<Button>().interactable = false;
        cardSlots[_first].GetComponent<Button>().interactable = false;

        _revealed[idx] = true;
        _revealed[_first] = true;
        _first = -1;
    }
}
